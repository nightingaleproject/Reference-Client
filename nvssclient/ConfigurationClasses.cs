namespace NVSSClient
{// ConfigurationClasses.cs

    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq; // For .Select() used in IValidatableObject

    public class AppConfig : IValidatableObject // Top-level config, needs to validate its complex properties
    {
        [Required(ErrorMessage = "The 'Kestrel' section is required.")]
        public KestrelSettings Kestrel { get; set; }

        [Required(ErrorMessage = "The 'ConnectionStrings' section is required.")]
        public ConnectionStringsSettings ConnectionStrings { get; set; }

        [Required(ErrorMessage = "The 'Authentication' section is required.")]
        public AuthenticationSettings Authentication { get; set; }

        [Required(ErrorMessage = "The 'JurisdictionEndpoint' is required.")]
        [Url(ErrorMessage = "The 'JurisdictionEndpoint' must be a valid URL.")]
        public string JurisdictionEndpoint { get; set; }

        [Required(ErrorMessage = "The 'LocalTesting' setting is required.")]
        [RegularExpression("^(True|False)$", ErrorMessage = "The value must be True or False.")]
        public string LocalTesting { get; set; }

        [Required(ErrorMessage = "The 'ResendInterval' setting is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "The 'ResendInterval' must be a positive integer.")]
        public int ResendInterval { get; set; }

        [Required(ErrorMessage = "The 'PollingInterval' setting is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "The 'PollingInterval' must be a positive integer.")]
        public int PollingInterval { get; set; }

        // Implement IValidatableObject to manually trigger validation for nested complex objects
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Validate KestrelSettings
            if (Kestrel != null)
            {
                var results = new List<ValidationResult>();
                var context = new ValidationContext(Kestrel, serviceProvider: null, items: null);
                if (!Validator.TryValidateObject(Kestrel, context, results, validateAllProperties: true))
                {
                    foreach (var result in results)
                    {
                        yield return new ValidationResult(result.ErrorMessage, result.MemberNames.Select(name => $"{nameof(Kestrel)}.{name}"));
                    }
                }
            }

            // Validate ConnectionStringsSettings
            if (ConnectionStrings != null)
            {
                var results = new List<ValidationResult>();
                var context = new ValidationContext(ConnectionStrings, serviceProvider: null, items: null);
                if (!Validator.TryValidateObject(ConnectionStrings, context, results, validateAllProperties: true))
                {
                    foreach (var result in results)
                    {                       //
                       yield return new ValidationResult(result.ErrorMessage, result.MemberNames.Select(name => $"{nameof(ConnectionStrings)}.{name}"));
                    }
             
                }
            }

            // Validate AuthenticationSettings
            if (Authentication != null)
            {
                var results = new List<ValidationResult>();
                var context = new ValidationContext(Authentication, serviceProvider: null, items: null);
                if (!Validator.TryValidateObject(Authentication, context, results, validateAllProperties: true))
                {
                    foreach (var result in results)
                    {
                        yield return new ValidationResult(result.ErrorMessage, result.MemberNames.Select(name => $"{nameof(Authentication)}.{name}"));
                    }
                }
            }
            // No need to validate JurisdictionEndpoint, LocalTesting, ResendInterval, PollingInterval here,
            // as their attributes are directly applied to properties of AppConfig and handled by ValidateDataAnnotations.
        }
    }

    public class KestrelSettings : IValidatableObject
    {
        [Required(ErrorMessage = "The 'Kestrel:EndPoints' section is required.")]
        public EndPointsSettings EndPoints { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Validate EndPointsSettings
            if (EndPoints != null)
            {
                var results = new List<ValidationResult>();
                var context = new ValidationContext(EndPoints, serviceProvider: null, items: null);
                if (!Validator.TryValidateObject(EndPoints, context, results, validateAllProperties: true))
                {
                    foreach (var result in results)
                    {
                        // Prepend EndPoints. to the member names for clear error messages
                        yield return new ValidationResult(result.ErrorMessage, result.MemberNames.Select(name => $"{nameof(EndPoints)}.{name}"));
                    }
                }
            }
        }
    }

    public class EndPointsSettings : IValidatableObject
    {
        [Required(ErrorMessage = "The 'Kestrel:EndPoints:Http' section is required.")]
        public HttpSettings Http { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Validate HttpSettings
            if (Http != null)
            {
                var results = new List<ValidationResult>();
                var context = new ValidationContext(Http, serviceProvider: null, items: null);
                if (!Validator.TryValidateObject(Http, context, results, validateAllProperties: true))
                {
                    foreach (var result in results)
                    {
                        // Prepend Http. to the member names
                        yield return new ValidationResult(result.ErrorMessage, result.MemberNames.Select(name => $"{nameof(Http)}.{name}"));
                    }
                }
            }
        }
    }

    public class HttpSettings
    {
        [Required(ErrorMessage = "The 'Kestrel:EndPoints:Http:Url' is required.")]
        [Url(ErrorMessage = "The 'Kestrel:EndPoints:Http:Url' must be a valid URL.")]
        public string Url { get; set; }
    }

    public class AuthenticationSettings
    {
        [Required(ErrorMessage = "The 'Authentication:ClientId' is required.")]
        public string ClientId { get; set; }

        [Required(ErrorMessage = "The 'Authentication:ClientSecret' is required.")]
        public string ClientSecret { get; set; }

        [Required(ErrorMessage = "The 'Authentication:Username' is required.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "The 'Authentication:Password' is required.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "The 'Authentication:Scope' is required.")]
        public string Scope { get; set; }
    }

    public class ConnectionStringsSettings
    {
        [Required(ErrorMessage = "The 'ConnectionStrings:ClientDatabase' is required.")]
        public string ClientDatabase { get; set; }

        [Required(ErrorMessage = "The 'ConnectionStrings:AuthServer' is required.")]
        [Url(ErrorMessage = "The 'ConnectionStrings:AuthServer' must be a valid URL.")]
        public string AuthServer { get; set; }

        [Required(ErrorMessage = "The 'ConnectionStrings:ApiServer' is required.")]
        [Url(ErrorMessage = "The 'ConnectionStrings:ApiServer' must be a valid URL.")]
        public string ApiServer { get; set; }

        [Required(ErrorMessage = "The 'ConnectionStrings:LocalServer' is required.")]
        [Url(ErrorMessage = "The 'ConnectionStrings:LocalServer' must be a valid URL.")]
        public string LocalServer { get; set; }
    }

}
