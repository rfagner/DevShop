using System;
using System.IO;
using System.Text;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2.Model;
using Model;
using Compartilhado;
using System.Threading.Tasks;
using System.Linq;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon;
using System.Collections.Generic;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Coletor
{
    public class Function
    {
        public async Task FunctionHandler(DynamoDBEvent dynamoEvent, ILambdaContext context)
        {         
            foreach (var record in dynamoEvent.Records)
            {              	
				if(record.EventName == "INSERT")
                {
                    var pedido = record.Dynamodb.NewImage.ToObject<Pedido>();
                    pedido.Status = StatusDoPedido.Coletado;

                    try
                    {                        
                        await ProcessarValorDoPedido(pedido);
                    }
                    catch (Exception ex)
                    {                        
                        pedido.JustificativaDeCancelamento = ex.Message;
                        pedido.Cancelado = true;
                        // Adicionar � fila de falha
                    }
                    
                    await pedido.SalvarAsync();                    
                }
            }                        
        }

        private async Task ProcessarValorDoPedido(Pedido pedido)
        {
            foreach(var produto in pedido.Produtos)
            {
                var ProdutoDoEstoque = await ObterProdutoDoDynamoDBAsync(produto.Id);
                if (ProdutoDoEstoque == null) throw new InvalidOperationException($"Produto n�o encontrado na tabela estoque. {produto.Id}");
               
                produto.Valor = ProdutoDoEstoque.Valor;
                produto.Nome = ProdutoDoEstoque.Nome;
            }

            var valorTotal = pedido.Produtos.Sum(x => x.Valor * x.Quantidade);
            if (pedido.ValorTotal != 0 && pedido.ValorTotal != valorTotal)
                throw new InvalidOperationException($"O valor esperado do pedido � de R$ {pedido.ValorTotal} e o valor verdadeiro � R$ {valorTotal}");

            pedido.ValorTotal = valorTotal;
        }

        private async Task<Produto> ObterProdutoDoDynamoDBAsync(string id)
        {
            var client = new AmazonDynamoDBClient(RegionEndpoint.SAEast1);            
            var request = new QueryRequest
            {
                TableName = "estoque",
                KeyConditionExpression = "Id = :v_id",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { { "v_id", new AttributeValue { S = id } } }
            };
            
            var response = await client.QueryAsync(request);
            var item = response.Items.FirstOrDefault();
            if (item == null) return null;
            return item.ToObject<Produto>();
        }
    }
}