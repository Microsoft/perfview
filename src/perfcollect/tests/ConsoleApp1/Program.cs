using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    class Program
    {
        static void StartException(object ignored)
        {
            try
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
                throw new Exception();
            }
            catch
            {
            }
        }
        static void RunException()
        {
            while (true)
            {
                for (int i = 0; i < 10; i++)
                {
                    var thread = new Thread(StartException);
                    thread.IsBackground = true;
                    thread.Start();
                }
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }
        }

        static void RunGC()
        {

            while (true)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                GC.Collect();
            }
        }


        static void Main(string[] args)
        {
            Console.WriteLine("PID: " + Process.GetCurrentProcess().Id);

            Task.Run(RunGC);
            Task.Run(RunException);
            while (true)
            {
            }
        }
    }
}
