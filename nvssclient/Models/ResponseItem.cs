using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{
    public class ResponseItem : BaseEntity
    {
        public long Id { get; set; }
        public String Uid { get; set; }
        public String StateAuxiliaryIdentifier { get; set; }
        public uint? CertificateNumber { get; set; }
        public uint? DeathYear {get; set;}
        public String DeathJurisdictionID { get; set; }
        public String Message {get; set;}
    }
}