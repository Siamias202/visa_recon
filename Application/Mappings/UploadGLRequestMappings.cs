using CsvHelper.Configuration;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Mappings
{
    public sealed class UploadGLRequestMappings : ClassMap<UploadGLRequest>
    {
        public UploadGLRequestMappings()
        {
            Map(m => m.AccountNo).Name("ACCOUNT NO");
            Map(m => m.PostingDate).Name("POSTING DATE");
            Map(m => m.ValueDate).Name("VALUE DATE");
            Map(m => m.BatchId).Name("BATCH ID");
            Map(m => m.PostingBranch).Name("POSTING BRANCH");
            Map(m => m.UniqueReferenceNo).Name("UNIQUEREFERENCENO");
            Map(m => m.DebitCredit).Name("DEBIT/CREDIT");
            Map(m => m.Amount).Name("AMOUNT");
            Map(m => m.TransactionCode).Name("TRANSACTION CODE");
            Map(m => m.TransactionName).Name("TRANSACTION NAME");
            Map(m => m.Currency).Name("CURRENCY");
            Map(m => m.TimeStamp).Name("TIME STAMP");
            Map(m => m.UniqueId).Name("UNIQUE ID");
            Map(m => m.Narrative1).Name("NARRATIVE 1");
            Map(m => m.Narrative2).Name("NARRATIVE 2");
            Map(m => m.RRN).Name("RRN");
            Map(m => m.AuthCode).Name("AUTH CODE");
            Map(m => m.Narrative3).Name("NARRATIVE 3");
            Map(m => m.Narrative4).Name("NARRATIVE 4");
        }
    }
}
