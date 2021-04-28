using ReportGenerator;
using System.Linq;
using System.IO;
using System;
using Newtonsoft.Json;

namespace DataFileGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            var rnd = new Random();

            File.WriteAllText(
                "students.json",
                JsonConvert.SerializeObject(
                    Enumerable.Range(1, 100).Select(i => new Student
                    {
                        Id = i.ToString(),
                        Name = i.ToString(),
                        Score = Convert.ToDecimal(rnd.Next(1, 9)) + (Convert.ToDecimal(rnd.Next(0, 100)) / 100m) // 0.00 ... 10.00
                    })
                    )
                );
        }
    }
}
