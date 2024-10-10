using System.Threading.Channels;
using TracyNET;

Console.WriteLine("Bye");

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
