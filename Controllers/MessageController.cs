using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NVSSClient.Models;
using Npgsql;
using VRDR;
using Microsoft.Extensions.DependencyInjection;

namespace NVSSClient.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MessageController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IServiceProvider Services;
        private static String cs = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";

        private static NpgsqlConnection con = new NpgsqlConnection(cs);

        public MessageController(AppDbContext context)
        {
            _context = context;
        }

        // DB functions
        public void InsertMessage(BaseMessage message, long recordId)
        {
            try 
            {
                // Create MessageItem
                MessageItem item = new MessageItem();
                item.Uid = message.MessageId;
                item.StateAuxiliaryIdentifier = message.StateAuxiliaryIdentifier;
                item.CertificateNumber = message.CertificateNumber;
                item.DeathJurisdictionID = message.DeathJurisdictionID;
                item.Record = recordId;
                item.Status = Models.MessageStatus.Sent;
                item.Retries = 0;
                item.SentOn = DateTime.UtcNow;

                // insert new message
                _context.MessageItems.Add(item);
                _context.SaveChanges();
                Console.WriteLine($"Inserted message {message.MessageId}");
            } catch (Exception e)
            {
                Console.WriteLine($"Error saving message {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
            }
        }

        public void UpdateMessageStatus(BaseMessage message, MessageStatus status)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                
                // insert new message
                var sql = "UPDATE message SET(status_id) VALUES (@status) WHERE state_auxiliary_id=@state AND cert_number=@cert AND nchs_id=@nchs;"; 
                
                using var cmd = new NpgsqlCommand(sql, con);
                // set status 
                cmd.Parameters.AddWithValue("status", ((int)status)); 
                
                // identifiers
                cmd.Parameters.AddWithValue("state", message.StateAuxiliaryIdentifier);
                cmd.Parameters.AddWithValue("cert", message.CertificateNumber);
                cmd.Parameters.AddWithValue("nchs", message.DeathJurisdictionID);

                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

        // Acknowledgements are relevant to specific messages, not a message series (coding response, updates)
        public void AcknowledgeMessage(AckMessage message)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                
                // insert new message
                var sql = "UPDATE message SET(status_id) VALUES (@status) WHERE uid=@uid;"; 
                using var cmd = new NpgsqlCommand(sql, con);

                // set the acked message to acknowledged
                cmd.Parameters.AddWithValue("status", ((int)MessageStatus.Acknowledged)); 
                cmd.Parameters.AddWithValue("uid", message.AckedMessageId);

                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

        public void UpdateMessageResponse(BaseMessage message, String response)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                // add response to the message
                var sql = "UPDATE message SET(response) VALUES (@response) WHERE state_auxiliary_id=@state AND cert_number=@cert AND nchs_id=@nchs;;"; 
                using var cmd = new NpgsqlCommand(sql, con);
                
                // set status to sent, will update if it fails
                cmd.Parameters.AddWithValue("response", response);
                
                // identifiers
                cmd.Parameters.AddWithValue("state", message.StateAuxiliaryIdentifier);
                cmd.Parameters.AddWithValue("cert", message.CertificateNumber);
                cmd.Parameters.AddWithValue("nchs", message.DeathJurisdictionID);
                
                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

        public static void UpdateMessageForResend(BaseMessage message)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                // add response to the message
                var sql = "UPDATE message SET last_submission=NOW(), retry = retry + 1, status = @status WHERE uid=@uid;"; 
                using var cmd = new NpgsqlCommand(sql, con);
                
                // set status to sent, will update if it fails
                cmd.Parameters.AddWithValue("status", ((int)MessageStatus.Sent)); 
                cmd.Parameters.AddWithValue("uid", message.MessageId);
                
                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

    }
}
