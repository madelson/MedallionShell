using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SampleCommand
{
    class Program
    {
        static void Main(string[] args)
        {
            Log("started: " + string.Join(", ", args.Select(a => "'" + a + "'")));

            string line;
            switch (args[0])
            {
                case "echo":
                    if (args.Length > 1 && args[1] == "--per-char")
                    {
                        int ch;
                        while ((ch = Console.In.Read()) != -1)
                        {
                            Console.Out.Write((char)ch);
                            Console.Out.Flush();
                        }
                    }
                    else
                    {
                        var input = Console.In.ReadToEnd();
                        Console.Out.Write(input);
                    }
                    break;
                case "errecho":
                    var errEchoInput = Console.In.ReadToEnd();
                    (args[0].StartsWith("err") ? Console.Error : Console.Out).Write(errEchoInput);
                    break;
                case "grep":
                    var regex = new Regex(args[1]);
                    while ((line = Console.ReadLine()) != null)
                    {
                        //Log("Read '{0}'", line);
                        if (regex.Match(line).Success)
                        {
                            Console.WriteLine(line);
                            //Log("Wrote '{0}'", line);
                        }
                    }
                    break;
                case "head":
                    var count = int.Parse(args[1]);
                    var i = 0;
                    while ((i++) < count && (line = Console.ReadLine()) != null)
                    {
                        Console.WriteLine(line);
                    }
                    break;
                case "exit":
                    var code = int.Parse(args[1]);
                    Environment.Exit(code);
                    break;
                case "argecho":
                    foreach (var argument in args.Skip(1))
                    {
                        Console.WriteLine(argument);
                    }
                    break;
                case "sleep":
                    Log("Sleeping for " + args[1]);
                    Thread.Sleep(int.Parse(args[1]));
                    break;
                case "bool":
                    Console.WriteLine(args[2]);
                    Console.Out.Flush();
                    if (!bool.Parse(args[1]))
                    {
                        Environment.Exit(1);
                    }
                    break;
                case "pipe":
                    string pipeLine;
                    while ((pipeLine = Console.In.ReadLine()) != null)
                    {
                        Console.Out.WriteLine(pipeLine);
                        Console.Out.Flush();
                    }
                    break;
                case "shortflush":
                    Console.Out.Write(args[1]);
                    Console.Out.Flush();
                    // don't exit until stdin closes
                    while (Console.ReadLine() != null)
                    {
                        Thread.Sleep(5);
                    }
                    break;
                default:
                    Console.Error.WriteLine("Unrecognized mode " + args[0]);
                    Environment.Exit(-1);
                    break;
            }

            Log("Exited normally");
        }

        [Conditional("TESTING")]
        public static void Log(string format, params object[] args)
        {
            var baseText = string.Format("{0:h:m:ss.fff} {1} ({2}): ", DateTime.Now, Process.GetCurrentProcess().Id, string.Join(" ", Environment.GetCommandLineArgs()));
            var text = baseText + string.Format(format, args);

            using (var fs = new FileStream("log.txt", FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(fs))
            {
                writer.WriteLine(text);
            }
        }
    }
}
