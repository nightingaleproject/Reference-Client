using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{
    public class PersistentState : BaseEntity
    {
        [Key]
        public String Name {get; set;}
        public String Value { get; set; }

    }


}