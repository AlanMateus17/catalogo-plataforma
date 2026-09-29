using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plataforma.Vendas;

/// <summary>O que cada plataforma precisa implementar para o site aceitar os avisos (webhooks) dela.</summary>
public interface IPlataformaVenda
{
    string Nome { get; }
    bool Configurada { get; }
    /// <summary>Confere se o aviso veio mesmo da plataforma (assinatura ou token).</summary>
    bool AssinaturaValida(HttpRequest req, string corpo);
    /// <summary>Chave única do aviso: o mesmo aviso reenviado não é processado duas vezes.</summary>
    string Chave(HttpRequest req, JsonNode corpo);
    string? TipoEvento(HttpRequest req, JsonNode corpo);
    /// <summary>Traduz o aviso para EventoVenda. Pode consultar a API da plataforma.</summary>
    Task<EventoVenda?> Traduzir(string consulta, JsonNode corpo);
}

static class Json
{
    // Lê um caminho como "data.buyer.email", sem diferenciar maiúsculas (Customer.email = customer.email)
    public static JsonNode? Em(this JsonNode? n, string caminho)
    {
        foreach (var parte in caminho.Split('.'))
        {
            if (n is not JsonObject o) return null;
            n = o.FirstOrDefault(kv => string.Equals(kv.Key, parte, StringComparison.OrdinalIgnoreCase)).Value;
        }
        return n;
    }
    public static string? Texto(this JsonNode? n, string caminho)
    {
        var v = n.Em(caminho);
        return v is null ? null : v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString().Trim('"');
    }
    public static decimal? Numero(this JsonNode? n, string caminho) =>
        decimal.TryParse(n.Texto(caminho), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static bool IgualSeguro(string? a, string? b) =>
        a is not null && b is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

// =====================================================================================
// KIWIFY — configure Vendas:Kiwify:Token com o token do webhook criado no painel da Kiwify.
// A Kiwify manda ?signature=<hex> na URL. Implementado como HMAC-SHA1 do corpo com o token.
// ATENÇÃO: confira com o botão de teste da Kiwify antes de vender (veja o LEIA-ME).
// =====================================================================================
public sealed class Kiwify(IConfiguration config) : IPlataformaVenda
{
    public string Nome => "kiwify";
    string? Token => config["Vendas:Kiwify:Token"];
    public bool Configurada => !string.IsNullOrWhiteSpace(Token);

    public bool AssinaturaValida(HttpRequest req, string corpo)
    {
        if (!Configurada) return false;
        var recebida = req.Query["signature"].ToString();
        var calculada = Convert.ToHexStringLower(HMACSHA1.HashData(Encoding.UTF8.GetBytes(Token!), Encoding.UTF8.GetBytes(corpo)));
        return Json.IgualSeguro(recebida.ToLowerInvariant(), calculada);
    }

    public string? TipoEvento(HttpRequest req, JsonNode c) => c.Texto("webhook_event_type") ?? c.Texto("order_status");

    public string Chave(HttpRequest req, JsonNode c) =>
        $"{c.Texto("order_id")}:{TipoEvento(req, c)}:{c.Texto("approved_date") ?? c.Texto("updated_at") ?? ""}";

    public Task<EventoVenda?> Traduzir(string consulta, JsonNode c)
    {
        var evento = (c.Texto("webhook_event_type") ?? "").ToLowerInvariant();
        var status = (c.Texto("order_status") ?? "").ToLowerInvariant();
        var tipo = evento switch
        {
            "order_approved" or "compra_aprovada" => "aprovado",
            "subscription_renewed" => "aprovado",
            "order_refunded" or "compra_reembolsada" => "reembolsado",
            "chargeback" or "order_chargedback" => "chargeback",
            "subscription_canceled" => "cancelado_assinatura",
            "subscription_late" => "atrasado",
            "billet_created" or "boleto_gerado" or "pix_created" or "pix_gerado" => "pendente",
            "order_rejected" or "compra_recusada" => "recusado",
            "carrinho_abandonado" or "abandoned_cart" => "ignorar",
            _ => status switch
            {
                "paid" or "approved" => "aprovado",
                "refunded" => "reembolsado",
                "chargedback" or "chargeback" => "chargeback",
                "waiting_payment" or "pending" => "pendente",
                "refused" or "rejected" => "recusado",
                _ => "ignorar",
            },
        };
        var pedido = c.Texto("order_id") ?? "";
        // Renovação de assinatura: o mesmo order_id com outra data vira outro pedido (outro período pago)
        if (evento == "subscription_renewed")
            pedido += ":" + (c.Texto("approved_date") ?? c.Texto("updated_at") ?? DateTime.UtcNow.ToString("yyyyMMdd"));
        var valor = c.Numero("Commissions.charge_amount") ?? c.Numero("Commissions.product_base_price") ?? 0;
        var email = c.Texto("Customer.email");
        if (string.IsNullOrWhiteSpace(email) || pedido == "") return Task.FromResult<EventoVenda?>(null);
        return Task.FromResult<EventoVenda?>(new EventoVenda("kiwify", tipo, pedido, email, c.Texto("Customer.full_name"),
            c.Texto("Product.product_id"), c.Texto("Subscription.plan.id"), (int)valor, c.Texto("subscription_id") ?? c.Texto("Subscription.id")));
    }
}

// =====================================================================================
// HOTMART — configure Vendas:Hotmart:Hottok com o Hottok (Ferramentas → Webhook no painel).
// A Hotmart manda o Hottok no cabeçalho X-HOTMART-HOTTOK: comparamos com o seu.
// =====================================================================================
public sealed class Hotmart(IConfiguration config) : IPlataformaVenda
{
    public string Nome => "hotmart";
    string? Hottok => config["Vendas:Hotmart:Hottok"];
    public bool Configurada => !string.IsNullOrWhiteSpace(Hottok);

    public bool AssinaturaValida(HttpRequest req, string corpo) =>
        Configurada && Json.IgualSeguro(req.Headers["X-HOTMART-HOTTOK"].ToString(), Hottok);

    public string? TipoEvento(HttpRequest req, JsonNode c) => c.Texto("event");
    public string Chave(HttpRequest req, JsonNode c) => c.Texto("id") ?? $"{c.Texto("data.purchase.transaction")}:{c.Texto("event")}";

    public Task<EventoVenda?> Traduzir(string consulta, JsonNode c)
    {
        var tipo = (c.Texto("event") ?? "").ToUpperInvariant() switch
        {
            "PURCHASE_APPROVED" or "PURCHASE_COMPLETE" => "aprovado",
            "PURCHASE_REFUNDED" => "reembolsado",
            "PURCHASE_CHARGEBACK" => "chargeback",
            "PURCHASE_BILLET_PRINTED" => "pendente",
            "PURCHASE_CANCELED" => "recusado",
            "PURCHASE_EXPIRED" => "expirado",
            "PURCHASE_DELAYED" => "atrasado",
            "SUBSCRIPTION_CANCELLATION" => "cancelado_assinatura",
            _ => "ignorar", // PURCHASE_PROTEST, SWITCH_PLAN etc.: ficam registrados para você conferir
        };
        var email = c.Texto("data.buyer.email") ?? c.Texto("data.subscriber.email");
        var transacao = c.Texto("data.purchase.transaction");
        if (tipo == "cancelado_assinatura") transacao ??= "assinatura:" + (c.Texto("data.subscription.subscriber.code") ?? c.Texto("data.subscriber.code"));
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(transacao)) return Task.FromResult<EventoVenda?>(null);
        var valor = c.Numero("data.purchase.price.value") ?? c.Numero("data.purchase.full_price.value") ?? 0;
        return Task.FromResult<EventoVenda?>(new EventoVenda("hotmart", tipo, transacao, email,
            c.Texto("data.buyer.name"), c.Texto("data.product.id"), c.Texto("data.purchase.offer.code"),
            (int)Math.Round(valor * 100), c.Texto("data.subscription.subscriber.code")));
    }
}

// =====================================================================================
// MERCADO PAGO — Vendas:MercadoPago:AccessToken (credenciais de produção) e
//                Vendas:MercadoPago:SegredoWebhook (Suas integrações → Webhooks).
// O aviso só traz o id; os dados são buscados na API, então ninguém consegue inventar um pagamento.
// =====================================================================================
public sealed class MercadoPago(IConfiguration config, IHttpClientFactory http, Dados.Banco banco) : IPlataformaVenda
{
    public string Nome => "mercadopago";
    string? Token => config["Vendas:MercadoPago:AccessToken"];
    string? Segredo => config["Vendas:MercadoPago:SegredoWebhook"];
    public bool Configurada => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(Segredo);

    public bool AssinaturaValida(HttpRequest req, string corpo)
    {
        if (!Configurada) return false;
        // x-signature: ts=...,v1=...   manifesto: id:[data.id];request-id:[x-request-id];ts:[ts];
        var partes = req.Headers["x-signature"].ToString().Split(',')
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        if (!partes.TryGetValue("ts", out var ts) || !partes.TryGetValue("v1", out var v1)) return false;
        var id = req.Query["data.id"].ToString().ToLowerInvariant();
        var requestId = req.Headers["x-request-id"].ToString();
        var manifesto = new StringBuilder();
        if (id != "") manifesto.Append($"id:{id};");
        if (requestId != "") manifesto.Append($"request-id:{requestId};");
        manifesto.Append($"ts:{ts};");
        var calc = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Segredo!), Encoding.UTF8.GetBytes(manifesto.ToString())));
        return Json.IgualSeguro(calc, v1.ToLowerInvariant());
    }

    public string? TipoEvento(HttpRequest req, JsonNode c) => c.Texto("type") ?? req.Query["type"].ToString();
    public string Chave(HttpRequest req, JsonNode c)
    {
        var rid = req.Headers["x-request-id"].ToString();
        return c.Texto("id") ?? (rid.Length > 0 ? rid : Guid.NewGuid().ToString());
    }

    HttpClient Cliente()
    {
        var c = http.CreateClient("mercadopago");
        c.BaseAddress = new Uri("https://api.mercadopago.com/");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return c;
    }

    public async Task<EventoVenda?> Traduzir(string consulta, JsonNode c)
    {
        var tipo = c.Texto("type") ?? c.Texto("topic");
        if (tipo != "payment") return null; // assinaturas do MP não são usadas (veja o LEIA-ME)
        var id = c.Texto("data.id");
        if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsLetterOrDigit)) return null;

        using var resp = await Cliente().GetAsync($"v1/payments/{id}");
        resp.EnsureSuccessStatusCode();
        var pag = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;

        if (!Guid.TryParse(pag.Texto("external_reference"), out var pedidoId)) return null; // não é venda deste site
        var pedido = await banco.Um("SELECT valor_centavos, email_comprador, nome_comprador FROM pedidos WHERE id=@id AND plataforma='mercadopago'",
            r => new { Valor = r.GetInt32(0), Email = r.GetString(1), Nome = r.IsDBNull(2) ? null : r.GetString(2) }, ("id", pedidoId));
        if (pedido is null) return null;

        var valor = (int)Math.Round((pag.Numero("transaction_amount") ?? 0) * 100);
        var status = pag.Texto("status");
        var ev = status switch
        {
            "approved" => "aprovado",
            "refunded" => "reembolsado",
            "charged_back" => "chargeback",
            "pending" or "in_process" or "authorized" or "in_mediation" => "pendente",
            "rejected" => "recusado",
            "cancelled" => "expirado",
            _ => "ignorar",
        };
        // Segurança extra: o valor pago precisa ser o valor do pedido criado pelo site
        if (ev == "aprovado" && (valor < pedido.Valor || pag.Texto("currency_id") != "BRL"))
            throw new InvalidOperationException($"Valor pago ({valor}) menor que o do pedido ({pedido.Valor}) ou moeda diferente. Confira manualmente.");

        return new EventoVenda("mercadopago", ev, id, pedido.Email, pedido.Nome, null, null, valor, null, pedidoId);
    }

    /// <summary>Cria a "preferência" (a página de pagamento do Mercado Pago) para um pedido do site.</summary>
    public async Task<string> CriarCheckout(Guid pedidoId, string titulo, int valorCentavos, string emailComprador, string urlPublica)
    {
        var corpo = new
        {
            items = new[] { new { id = pedidoId.ToString(), title = titulo, quantity = 1, unit_price = valorCentavos / 100m, currency_id = "BRL" } },
            payer = new { email = emailComprador },
            external_reference = pedidoId.ToString(),
            notification_url = $"{urlPublica}/api/webhooks/mercadopago",
            back_urls = new
            {
                success = $"{urlPublica}/conta.html?compra=aprovada",
                pending = $"{urlPublica}/conta.html?compra=pendente",
                failure = $"{urlPublica}/planos.html?compra=falhou",
            },
            auto_return = "approved",
            statement_descriptor = "CATALOGOHTML",
            // O link de pagamento vence em 24 h: depois disso o pedido expira e o uso do cupom volta
            expires = true,
            expiration_date_to = DateTimeOffset.UtcNow.AddHours(24).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
        };
        using var resp = await Cliente().PostAsJsonAsync("checkout/preferences", corpo);
        var texto = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("Mercado Pago recusou: " + texto);
        return JsonNode.Parse(texto).Texto("init_point") ?? throw new InvalidOperationException("Resposta sem init_point.");
    }
}
