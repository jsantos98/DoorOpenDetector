using System;

namespace DoorOpenDetector
{
   internal class Program
   {
      public Program()
      {
      }

      private static async Task Main(string[] args)
      {
         Console.WriteLine("Initializing application...");
         DoorDetector app = new DoorDetector();
         Console.WriteLine("App Created");
         app.Connect();

         bool bExit = false;
         while (!bExit)
         {
            try
            {
               Console.WriteLine("Starting");
               await app.Start();

               bExit = true;
               app.Stop();
            }
            catch (Exception exception)
            {
               Console.WriteLine(string.Concat("Exception!!!! ", exception.Message));
               Console.WriteLine(exception);
            }
         }
      }
   }
}