using CsvHelper.Configuration;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Mappings.GLMappingsHelper
{
    public sealed class UploadGLRequestMappings
     : ClassMap<UploadGLRequest>
    {
        public UploadGLRequestMappings()
        {
            Map(x => x.AccountNo)
                .Name("ACCOUNT NO")
                .TypeConverter<GlIdentifierConverter>();

            Map(x => x.PostingDate)
                .Name("POSTING DATE")
                .TypeConverter<GlDateConverter>();

            Map(x => x.ValueDate)
                .Name("VALUE DATE")
                .TypeConverter<GlDateConverter>();

            Map(x => x.BatchId)
                .Name("BATCH ID");

            Map(x => x.PostingBranch)
                .Name("POSTING BRANCH");

            Map(x => x.UniqueReferenceNo)
                .Name("UNIQUEREFERENCENO")
                .TypeConverter<GlIdentifierConverter>();

            Map(x => x.DebitCredit)
                .Name("DEBIT/CREDIT");

            Map(x => x.Amount)
                .Name("AMOUNT");

            Map(x => x.TransactionCode)
                .Name("TRANSACTION CODE");

            Map(x => x.TransactionName)
                .Name("TRANSACTION NAME");

            Map(x => x.Currency)
                .Name("CURRENCY");

            Map(x => x.TimeStamp)
                .Name("TIME STAMP")
                .TypeConverter<GlTimestampConverter>();

            Map(x => x.UniqueId)
                .Name("UNIQUE ID")
                .TypeConverter<GlIdentifierConverter>();

            Map(x => x.Narrative1)
                .Name("NARRATIVE 1");

            Map(x => x.Narrative2)
                .Name("NARRATIVE 2");

            Map(x => x.RRN)
                .Name("RRN")
                .TypeConverter<GlIdentifierConverter>();

            Map(x => x.AuthCode)
                .Name("AUTH CODE");

            Map(x => x.Narrative3)
                .Name("NARRATIVE 3");

            Map(x => x.Narrative4)
                .Name("NARRATIVE 4");
        }
    }
}

