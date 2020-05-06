using System.ServiceProcess;
using WindowsService_AlianceRacorder_sazonov;

namespace WindowsService_AlianceRecorder_sazonov
{
    static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new AlianceRacorder_sazonov()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
