using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{

    public enum MessageStatus 
    {
        Sent,
        Acknowledged,
        Error
    }
    public class MessageItem : BaseEntity
    {
        public long Id { get; set; }
        // [StringLength(50)]  
        // [Column(TypeName = "varchar(50)")]
        public String Uid { get; set; }
        // [StringLength(50)]  
        // [Column(TypeName = "varchar(50)")]
        public String StateAuxiliaryIdentifier { get; set; }
        public uint? CertificateNumber { get; set; }
        // [StringLength(50)]  
        // [Column(TypeName = "varchar(50)")]
        public String DeathJurisdictionID { get; set; }
        // [ForeignKey ("RecordItem")]
        public long Record { get; set; }
        public int Retries { get; set; }
        public MessageStatus Status { get; set; }   
        public DateTime ? SentOn { get; set; }
    }
}