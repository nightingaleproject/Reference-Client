using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{
    public class RecordItem : BaseEntity
    {
        public long Id { get; set; }
        public string Record { get; set; }
    }
}