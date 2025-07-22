using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{

    public enum MessageStatus 
    {
        Pending,
        Sent,
        Acknowledged,
        Error,
        AcknowledgedAndCoded
    }
    public class MessageItem : BaseEntity
    {
        public long Id { get; set; }
        public String Uid { get; set; }
        public String StateAuxiliaryIdentifier { get; set; }
        public uint? CertificateNumber { get; set; }
        public String JurisdictionID { get; set; }
        public uint? EventYear { get; set; }
        public String Message {get; set;}
        public int Retries { get; set; }
        public String Status { get; set; }   
        public DateTime ? ExpirationDate { get; set; }

        public String VitalRecordType { get; set; }
        public String IJE_Version { get; set; }
    }
}