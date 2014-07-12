using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SampleCommand
{
    class Program
    {
        static void Main(string[] args)
        {
            switch (args[0])
            {
                case "grep":
                    var regex = new Regex(args[1]);
                    string line;
                    while ((line = Console.ReadLine()) != null)
                    {
                        if (regex.Match(line).Success)
                        {
                            Console.WriteLine(line);
                        }
                    }
                    break;
                default:
                    Console.Error.WriteLine("Unrecognized mode " + args[0]);
                    Environment.Exit(-1);
                    break;
            }
        }
    }
}
