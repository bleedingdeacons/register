using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Exceptions
{
    public class TemplateRenderingException : Exception
    {
        public string? TemplateName { get; }

        public TemplateRenderingException(string message) : base(message)
        {
        }

        public TemplateRenderingException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public TemplateRenderingException(string message, string templateName) : base(message)
        {
            TemplateName = templateName;
        }

        public TemplateRenderingException(string message, string templateName, Exception innerException) : base(message, innerException)
        {
            TemplateName = templateName;
        }
    }

    public class TemplateNotFoundException : Exception
    {
        public string? TemplateName { get; }

        public TemplateNotFoundException(string templateName) : base($"Template '{templateName}' was not found.")
        {
            TemplateName = templateName;
        }

        public TemplateNotFoundException(string templateName, string message) : base(message)
        {
            TemplateName = templateName;
        }
    }
}

