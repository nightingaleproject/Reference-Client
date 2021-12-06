using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{
    public class ResponseItem : BaseEntity
    {
        public long Id { get; set; }
        // [StringLength(50)]  
        // [Column(TypeName = "varchar(50)")]
        public String Uid { get; set; }
        // [StringLength(50)]  
        // [Column(TypeName = "varchar(50)")]
        public String StateAuxiliaryIdentifier { get; set; }
        public uint? CertificateNumber { get; set; }
        public uint? DeathYear {get; set;}
        // [StringLength(50)]  
        // [Column(TypeName = "varchar(50)")]
        public String DeathJurisdictionID { get; set; }
        public String Message {get; set;}
    }
}