using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
    public interface IAttendanceRegistration<in T>
    {
        Task Register(T entity);

        Task Unregister(T entity);
    }
}
 