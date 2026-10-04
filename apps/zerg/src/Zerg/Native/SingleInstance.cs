using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Zerg.Native;

/// <summary>
/// One Zerg per signed-in user. Two would follow the same event file, write
/// the same settings over each other and lay the same panels over the game
/// twice, so a second launch does one thing: it tells the first to come
/// forward, and ends.
///
/// <para>A named mutex says whether there is a first; a named pipe carries
/// the message to it. Both are named per Windows session, so two people
/// signed in to one machine each have their own.</para>
/// </summary>
static class SingleInstance
{
    const string Name = "VibeXI.Zerg";

    /// <summary>Held for as long as the process lives; that is what makes it the first.</summary>
    static Mutex? mutex;

    static string PipeName => Name + "." + Process.GetCurrentProcess().SessionId;

    /// <summary>
    /// True if this is the only Zerg running, and from then on it is the one
    /// later launches find. Call before anything is read or written: a second
    /// launch must leave the log and the settings to the first.
    /// </summary>
    public static bool Claim()
    {
        mutex = new Mutex(initiallyOwned: true, @"Local\" + Name, out bool first);
        return first;
    }

    /// <summary>
    /// From a second launch: asks the first to come forward. Windows lets a
    /// program put its window in front only when the program in front agrees,
    /// and for a moment after being started that is this one, so it passes
    /// the right on before it asks.
    /// </summary>
    public static void Signal()
    {
        AllowSetForegroundWindow(ASFW_ANY);
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            // The first may itself be only just starting, its pipe not yet open.
            pipe.Connect(3000);
            pipe.WriteByte(1);
        }
        // Nothing listening: the first is on its way out, or stuck. There is
        // nobody to tell and nowhere this launch may write that it happened.
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// In the first: listens for later launches for as long as the process
    /// lives. <paramref name="cameAgain"/> is called on a background thread.
    /// </summary>
    public static void Listen(Action cameAgain)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                                                               PipeOptions.CurrentUserOnly);
                    pipe.WaitForConnection();
                    if (pipe.ReadByte() >= 0) cameAgain();
                }
                catch (IOException)
                {
                    // A launch that went away mid-word. The next one is listened for.
                }
                catch (UnauthorizedAccessException e)
                {
                    // The name is taken by something that is not Zerg. A second
                    // launch will find nothing to bring forward, and that is all.
                    Log.Write("single instance: can't listen for a second launch (" + e.Message + ")");
                    return;
                }
            }
        })
        { IsBackground = true, Name = "second launch" };
        thread.Start();
    }

    const uint ASFW_ANY = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AllowSetForegroundWindow(uint processId);
}
