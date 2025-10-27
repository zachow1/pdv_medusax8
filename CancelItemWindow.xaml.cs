using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PDV_MedusaX8
{
    public partial class CancelItemWindow : Window
    {
        public class CancelOption
        {
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public double AvailableQty { get; set; }
            public decimal UnitPrice { get; set; }
            public bool Cancel { get; set; }
            public double QtyToCancel { get; set; } = 0d;
        }

        public List<CancelOption> Options { get; } = new();
        public List<(string Code, string Description, double Qty, decimal UnitPrice)> Result { get; private set; } = new();

        public CancelItemWindow(IEnumerable<CancelOption> options)
        {
            InitializeComponent();
            Options = options.Select(o => new CancelOption
            {
                Code = o.Code,
                Description = o.Description,
                AvailableQty = o.AvailableQty,
                UnitPrice = o.UnitPrice,
                Cancel = false,
                QtyToCancel = 0d
            }).ToList();
            DgvCancel.ItemsSource = Options;
            App.Log($"CancelItemWindow aberta options={Options.Count}");
        }

        private void DgvCancel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var removed in e.RemovedItems.OfType<CancelOption>())
            {
                removed.Cancel = false;
                App.Log($"Cancelar desmarcado Code={removed.Code}");
            }
            foreach (var added in e.AddedItems.OfType<CancelOption>())
            {
                added.Cancel = true;
                if (added.QtyToCancel <= 0)
                {
                    var suggested = Math.Min(1.0, added.AvailableQty);
                    added.QtyToCancel = suggested;
                    App.Log($"Cancelar marcado Code={added.Code} sugeridoQty={suggested}");
                }
            }
            DgvCancel.Items.Refresh();
        }

        private void DgvCancel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgvCancel.SelectedItem is CancelOption sel)
            {
                App.Log($"Cancelar double-click Code={sel.Code} qty={sel.QtyToCancel} disponivel={sel.AvailableQty}");
                if (sel.QtyToCancel <= 0)
                {
                    MessageBox.Show($"Informe uma quantidade válida para o item {sel.Description}.", "Cancelar Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                    App.Log($"Cancelar inválido qty<=0 Code={sel.Code}");
                    return;
                }
                if (sel.QtyToCancel > sel.AvailableQty + 1e-9)
                {
                    MessageBox.Show($"Quantidade a cancelar maior que disponível para {sel.Description}.", "Cancelar Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                    App.Log($"Cancelar inválido qty>disponivel Code={sel.Code}");
                    return;
                }
                Result.Clear();
                Result.Add((sel.Code, sel.Description, sel.QtyToCancel, sel.UnitPrice));
                App.Log($"Cancelar confirmado unico Code={sel.Code} qty={sel.QtyToCancel}");
                DialogResult = true;
                Close();
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Result.Clear();
            foreach (var opt in Options)
            {
                if (!opt.Cancel) continue;
                if (opt.QtyToCancel <= 0)
                {
                    MessageBox.Show($"Quantidade inválida para o item {opt.Description}.", "Cancelar Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                    App.Log($"Cancelar inválido qty<=0 Code={opt.Code}");
                    return;
                }
                if (opt.QtyToCancel > opt.AvailableQty + 1e-9)
                {
                    MessageBox.Show($"Quantidade a cancelar maior que disponível para {opt.Description}.", "Cancelar Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                    App.Log($"Cancelar inválido qty>disponivel Code={opt.Code}");
                    return;
                }
                Result.Add((opt.Code, opt.Description, opt.QtyToCancel, opt.UnitPrice));
                App.Log($"Cancelar marcado Code={opt.Code} qty={opt.QtyToCancel}");
            }

            if (Result.Count == 0)
            {
                MessageBox.Show("Nenhum item selecionado para cancelamento.", "Cancelar Item", MessageBoxButton.OK, MessageBoxImage.Information);
                App.Log("Cancelar sem seleção");
                return;
            }

            App.Log($"Cancelar confirmado multi itens={Result.Count}");
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            App.Log("Cancelar janela fechada (BtnClose)");
            DialogResult = false;
            Close();
        }
    }
}