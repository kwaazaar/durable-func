using System;
using System.Collections.Generic;
using System.Text;

namespace ReportGenerator
{
    public class ArchiveReportCommand
    {
        public string Base64Data { get; set; }
        public string Filename { get; set; }
        public string Mimetype { get; set; }
    }
}
