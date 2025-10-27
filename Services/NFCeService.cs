using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8;
using PDV_MedusaX8.Models;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8.Services
{
    public class NFCeService
    {
        private string GetConnectionString() => PDV_MedusaX8.Services.DbHelper.GetConnectionString();

        public void EmitirNFCe(MainWindow mw, List<CartItem> items, decimal saleTotal, string? consumidorNome, string? consumidorCPF, List<AppliedItem> payments)
        {
            using var conn = new SqliteConnection(GetConnectionString());
            conn.Open();

            // Carrega configurações
            int tpAmb = 2;
            int serie = 1;
            int proximoNumero = 1;
            string? cscId = null;
            string? csc = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT TpAmb, CSCId, CSC, Serie, ProximoNumero FROM ConfiguracoesNFCe WHERE Id=1;";
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    tpAmb = r.IsDBNull(0) ? 2 : r.GetInt32(0);
                    cscId = r.IsDBNull(1) ? null : r.GetString(1);
                    csc = r.IsDBNull(2) ? null : r.GetString(2);
                    serie = r.IsDBNull(3) ? 1 : r.GetInt32(3);
                    proximoNumero = r.IsDBNull(4) ? 1 : r.GetInt32(4);
                }
            }

            int cashRegister = 1;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key='CashRegisterNumber' LIMIT 1;";
                var obj = cmd.ExecuteScalar();
                if (obj != null && obj != DBNull.Value && int.TryParse(Convert.ToString(obj), out var parsed))
                {
                    cashRegister = parsed;
                }
            }

            // Cabeçalho NFC-e
            decimal totalProdutos = items.Sum(i => i.Total);
            long nfcId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO NFCe (Numero, Serie, DataEmissao, Status, TotalProdutos, Total, ConsumidorCPF, ConsumidorNome)
                                    VALUES ($num, $serie, datetime('now'), $status, $totProd, $tot, $cpf, $nome);";
                cmd.Parameters.AddWithValue("$num", proximoNumero);
                cmd.Parameters.AddWithValue("$serie", serie);
                cmd.Parameters.AddWithValue("$status", "Pendente");
                cmd.Parameters.AddWithValue("$totProd", totalProdutos);
                cmd.Parameters.AddWithValue("$tot", saleTotal);
                cmd.Parameters.AddWithValue("$cpf", string.IsNullOrWhiteSpace(consumidorCPF) ? (object)DBNull.Value : consumidorCPF);
                cmd.Parameters.AddWithValue("$nome", string.IsNullOrWhiteSpace(consumidorNome) ? (object)DBNull.Value : consumidorNome);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT last_insert_rowid();";
                nfcId = (long)(cmd.ExecuteScalar() ?? 0L);
            }

            // Itens NFC-e
            using (var tx = conn.BeginTransaction())
            {
                foreach (var it in items)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO NFCeItem (NFCeId, Codigo, Descricao, Qt, VlUnit, VlTotal)
                                          VALUES ($id, $cod, $desc, $qt, $unit, $tot);";
                    cmd.Parameters.AddWithValue("$id", nfcId);
                    cmd.Parameters.AddWithValue("$cod", it.Codigo ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$desc", it.Descricao ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$qt", it.Qt);
                    cmd.Parameters.AddWithValue("$unit", it.VlUnit);
                    cmd.Parameters.AddWithValue("$tot", it.Total);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }

            // Pagamentos NFC-e
            using (var txp = conn.BeginTransaction())
            {
                foreach (var p in payments)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = txp;
                    cmd.CommandText = @"INSERT INTO NFCePagamento (NFCeId, tPag, vPag, CNPJ_INTERMED, TID, NSU, Bandeira)
                                        VALUES ($id, $tPag, $vPag, NULL, NULL, NULL, NULL);";
                    cmd.Parameters.AddWithValue("$id", nfcId);
                    cmd.Parameters.AddWithValue("$tPag", string.IsNullOrWhiteSpace(p.PaymentCode) ? (object)DBNull.Value : p.PaymentCode);
                    cmd.Parameters.AddWithValue("$vPag", p.Value);
                    cmd.ExecuteNonQuery();
                }
                txp.Commit();
            }

            // Avança numeração
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE ConfiguracoesNFCe SET ProximoNumero = $next WHERE Id=1;";
                cmd.Parameters.AddWithValue("$next", proximoNumero + 1);
                cmd.ExecuteNonQuery();
            }

            // TODO: integrar ACBrLib para montar XML, assinar e enviar
            // Por ora, apenas persiste os dados mínimos para validação.
        }
    }
}