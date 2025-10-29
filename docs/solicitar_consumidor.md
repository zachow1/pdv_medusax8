# Comportamento: "Solicitar consumidor" (PromptConsumerOnFirstItem)

Este documento descreve o comportamento esperado do checkbox "Solicitar consumidor" considerando os modos com e sem exigência de F2 para iniciar a venda.

## Definições
- `PromptConsumerOnFirstItem` (checkbox): Quando ativo, o sistema solicita os dados do consumidor no início da venda.
- `RequireF2ToStartSale` (checkbox): Quando ativo, é necessário pressionar `F2` para iniciar a venda.
- `HasConsumer`: Verdadeiro quando já existe um consumidor selecionado para a venda atual.

## Regras de comportamento

- F2 exigido = `true`, Solicitar consumidor = `true`
  - Ao pressionar `F2`: a venda é iniciada e o sistema solicita o consumidor imediatamente se `HasConsumer == false`.
  - Ao adicionar itens: não solicita (já foi sugerido no início da venda).

- F2 exigido = `true`, Solicitar consumidor = `false`
  - Ao pressionar `F2`: a venda é iniciada e o sistema não solicita consumidor.
  - Ao adicionar itens: não solicita.

- F2 exigido = `false`, Solicitar consumidor = `true`
  - Na primeira interação (ex.: clique/foco no campo de código) com carrinho vazio: solicita consumidor se `HasConsumer == false`.
  - Na adição do primeiro item (código digitado/enter, scanner, F8, pesquisa): solicita consumidor se `HasConsumer == false`.

- F2 exigido = `false`, Solicitar consumidor = `false`
  - Nunca solicita consumidor automaticamente.

## Casos especiais
- Se já houver consumidor selecionado (`HasConsumer == true`), nenhuma solicitação adicional ocorre.
- Ao finalizar a venda, o consumidor é limpo para a próxima venda.
- Alterações em Configurações são aplicadas ao fechar a janela de Configurações, sem necessidade de reiniciar o PDV.

## Locais relevantes no código
- `MainWindow.xaml.cs`
  - Atalho `F2` (início da venda): respeita `PromptConsumerOnFirstItem` antes de abrir o diálogo do consumidor.
  - `InputCodeField_PreviewMouseDown`: aciona a solicitação quando F2 não é exigido, carrinho vazio e opção ativa.
  - `AddOrMergeCartItem`: aciona a solicitação no primeiro item quando F2 não é exigido e opção ativa.
  - `LoadPromptConsumerFlag` e `LoadRequireF2StartSaleFlag`: carregam as flags de Configurações.
- `SettingsWindow.xaml.cs`
  - Leitura/gravação das chaves `PromptConsumerOnFirstItem` e `RequireF2ToStartSale` em `Settings`.

## Cenários de teste recomendados
1) F2 exigido ON, Solicitar consumidor ON
   - Pressione `F2` com carrinho vazio e sem consumidor: deve solicitar.
   - Adicione primeiro item após iniciar: não deve solicitar novamente.

2) F2 exigido ON, Solicitar consumidor OFF
   - Pressione `F2`: não solicita.
   - Adicione itens: não solicita.

3) F2 exigido OFF, Solicitar consumidor ON
   - Clique no campo de código com carrinho vazio e sem consumidor: deve solicitar.
   - Se ignorar, adicione o primeiro item por: digitação+Enter, scanner, F8, pesquisa de produtos: deve solicitar.

4) F2 exigido OFF, Solicitar consumidor OFF
   - Nenhuma solicitação automática ao clicar no campo ou adicionar itens.

5) Consumidor já selecionado
   - Em qualquer combinação acima: não deve solicitar novamente.

6) Após finalizar a venda
   - Consumidor deve ser limpo e a próxima venda segue as regras acima.