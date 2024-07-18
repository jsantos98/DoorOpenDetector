using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoorOpenDetector.Objects
{
   internal class ZigBeePayload<T>
   {
      public Dictionary<string, T> ZbReceived { get; set; }

      public T Get()
      {
         return ((ZbReceived ?? new Dictionary<string, T>()).FirstOrDefault().Value);
      }
   }
}
