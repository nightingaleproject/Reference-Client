using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{
    public class ResponseItem : BaseEntity
    {
        public long Id { get; set; }
        public String Uid { get; set; }
        public String ReferenceUid { get; set; }
        public String StateAuxiliaryIdentifier { get; set; }
        public uint? CertificateNumber { get; set; }
        
        public uint? EventYear { get; set; }
        public String JurisdictionID { get; set; }
        public String Message {get; set;}
        public String VitalRecordType { get; set; }
        public String IGVersion { get; set; }
    }
}