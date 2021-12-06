using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{

    public enum MessageStatus 
    {
        Pending,
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
        public uint? DeathYear {get; set;}
        public String Message {get; set;}
        public int Retries { get; set; }
        public MessageStatus Status { get; set; }   
        public DateTime ? ExpirationDate { get; set; }
    }
}