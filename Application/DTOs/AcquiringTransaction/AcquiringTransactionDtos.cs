namespace VISA_RECON.API.Application.DTOs.AcquiringTransaction;

public sealed class AcquiringDetailsRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SearchQuery { get; set; }
}

public sealed class AcquiringFeTransaction
{
    public long Id { get; set; }
    public string AtmId { get; set; } = string.Empty;
    public bool Reversal { get; set; }
    public decimal RequestAmount { get; set; }
    public int Bills1 { get; set; }
    public int Bills2 { get; set; }
    public int Bills3 { get; set; }
    public int Bills4 { get; set; }
    public int Udate { get; set; }
    public string Time { get; set; } = string.Empty;
    public string UtrNo { get; set; } = string.Empty;
    public string IssuerInst { get; set; } = string.Empty;
    public string ReferenceNum { get; set; } = string.Empty;
    public string AuthCode { get; set; } = string.Empty;
    public string Acct1 { get; set; } = string.Empty;
    public string HpanCard { get; set; } = string.Empty;
}

public sealed class AcquiringEpTransaction
{
    public long Id { get; set; }
    public string Pan { get; set; } = string.Empty;
    public string Rrn { get; set; } = string.Empty;
    public string Acq { get; set; } = string.Empty;
    public string Integratedp { get; set; } = string.Empty;
    public string Aymen { get; set; } = string.Empty;
    public string Tsyste { get; set; } = string.Empty;
    public string M { get; set; } = string.Empty;
    public decimal AmountBdt { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AmountUsd { get; set; }
}
