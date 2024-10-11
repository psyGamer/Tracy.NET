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
