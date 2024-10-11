using System.Threading.Channels;
using TracyNET;

// Tracy.SetProgramName("My Amazing Program");
// Tracy.SetThreadName("My Cool Thread");
//
// while (true)
// {
//     Console.WriteLine("Outside Frame");
//
//     Tracy.MarkFrameStart();
//
//     Console.WriteLine("Inside Frame");
//
//     using (Tracy.Zone("A lot of work"))
//     {
//         Thread.Sleep(10); // Simulate work
//     }
//
//     Tracy.MarkFrameEnd();
// }

public class Program
{
    public static void Main()
    {
        while (true)
        {
            MyMethod();

            using (Tracy.Zone("My Custom Zone"))
            {
                // Work
                Thread.Sleep(10);
            }

            Console.WriteLine("Separator 1");

            using (Tracy.ZoneContext zone1 = Tracy.Zone("My Custom Zone 1/1"), zone2 = Tracy.Zone("My Custom Zone 1/2"))
            {
                // Work
                Thread.Sleep(10);
            }

            Console.WriteLine("Separator 2");

            using (Tracy.Zone("My Custom Zone 2/1"))
            using (Tracy.Zone("My Custom Zone 2/2"))
            {
                // Work
                Thread.Sleep(10);
            }

            Console.WriteLine("Separator 3");

            using var zone3 = Tracy.Zone("My Custom Zone 3");
            // Work
            Thread.Sleep(10);
        }
    }

    [Tracy.ProfileMethod]
    static void MyMethod()
    {
        Console.WriteLine($"hi {Type.GetType("TracyNET.Tracy")}");
    }
}



// [Tracy.ProfileMethod]
// void MyMethod()
// {
//     object a = 2;
//     Type b;
//
//     Console.WriteLine($"hi {Type.GetType("TracyNET.Tracy")}");
// }
