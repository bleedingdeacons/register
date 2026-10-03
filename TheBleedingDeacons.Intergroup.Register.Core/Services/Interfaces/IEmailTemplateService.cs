using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
    public interface IEmailTemplateService
    {
        Task<string> RenderTemplateAsync<T>(string templateName, T model);
        Task<string> RenderTemplateFromStringAsync<T>(string template, T model);
        string RenderTemplate<T>(string template, T model);
    }
}
