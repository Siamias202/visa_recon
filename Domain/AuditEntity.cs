namespace VISA_RECON.API.Domain.Entities
{
    public class AuditEntity
    {
        public string CreatedBy { get; set; } = default!;
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; } = default!;
        public DateTime ModifiedOn { get; set; }
    }
}
