using System;
using System.IO;

namespace TestBIM
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            string xml = File.ReadAllText("../../../sample.xml");
            RoomScanAPI.ParseRoomScanXML.Create(xml);
        }
    }
}
