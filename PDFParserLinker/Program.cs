using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace PDFParserLinker
{
    class Program
    {
        static string ExampleLocation = @"c:\temp\example.pdf";
        static void Main(string[] args)
        {
            var found = ParsePdfText.GetUnderlinedAndBolded(ExampleLocation);
            Console.WriteLine(string.Join("\r\n", found));
        }
    }
    public static class ParsePdfText
    {
        public static string[] GetUnderlinedAndBolded(string filename)
        {
            var strategies = new List<UnderlinedBoldedLocationTextExtractionStrategy>();
            using (var reader = new PdfReader(filename))
                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    var strategy = new UnderlinedBoldedLocationTextExtractionStrategy();
                    strategies.Add(strategy);
                    PdfTextExtractor.GetTextFromPage(reader, page, strategy);
                }
            return strategies.SelectMany(s => s.GetResultantText().Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)).ToArray();
        }
    }
    public class UnderlinedBoldedLocationTextExtractionStrategy : LocationTextExtractionStrategy
    {
        //Keep info on found parts
        protected readonly List<TextChunkInfo> FoundItems = new List<TextChunkInfo>();
        public UnderlinedBoldedLocationTextExtractionStrategy()
        {
            BoldAndUnderlinedText = new List<string>();
        }

        private List<string> BoldAndUnderlinedText { get; }

        private FieldInfo _gsField = typeof(TextRenderInfo).GetField("gs",
            BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly FieldInfo _locationResultField =
            typeof(LocationTextExtractionStrategy).GetField("locationResult",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private List<TextChunkInfo> _allLocations = new List<TextChunkInfo>();

        private readonly List<float> _lineHeights = new List<float>();
        //Automatically called for each chunk of text in the PDF
        public override void RenderText(TextRenderInfo renderInfo)
        {
            base.RenderText(renderInfo);
            //UNDONE:Need to determine if text is underlined.  How?

            //NOTE: renderInfo.GetFont().FontWeight does not contain any actual information
            var gs = (GraphicsState)_gsField.GetValue(renderInfo);
            var textChunkInfo = new TextChunkInfo(renderInfo);
            _allLocations.Add(textChunkInfo);
            if (gs.Font.PostscriptFontName.Contains("Bold"))
                //Add this to our found collection
                FoundItems.Add(new TextChunkInfo(renderInfo));

            if (!_lineHeights.Contains(textChunkInfo.LineHeight))
                _lineHeights.Add(textChunkInfo.LineHeight);
        }

        private static LineSegment GetSegment(TextRenderInfo info)
        {
            var segment = info.GetBaseline();
            if (Math.Abs(info.GetRise()) > 0.01)
                segment.TransformBy(new Matrix(0, -info.GetRise()));
            return segment;
        }

        public override string GetResultantText()
        {
            var sb = new StringBuilder();
            //sb.AppendLine(base.GetResultantText());
            //sb.AppendLine(new string('-', 40));
            TextChunkInfo lastFound = null;
            var leftEdge = _allLocations.Min(l => l.StartLocation[Vector.I1]);
            var rightEdge = _allLocations.Max(l => l.EndLocation[Vector.I1]);
            foreach (var info in FoundItems)
            {
                if (null == lastFound)
                    sb.Append(info.Text);
                else
                {
                    // Combine successive words
                    //  both inline
                    var inline = info.SameLine(lastFound) &&
                                 Math.Abs(info.DistanceFromEndOf(lastFound)) < 12;//HACK: Guessed at maxDistance
                    //TODO:  and split across lines
                    var splitAcrossLines = info.StartLocation[Vector.I1] - leftEdge < 2
                                           && rightEdge - lastFound.EndLocation[Vector.I1] < info.CharSpaceWidth + info.EndLocation[Vector.I1] - info.StartLocation[Vector.I1]//45
                                           && OnNextLine(info, lastFound);
                    if (inline || splitAcrossLines)
                        sb.Append(" ");
                    else
                        sb.Append("\r\n");
                    sb.Append(info.Text);
                }

                lastFound = info;
            }
            return sb.ToString();
        }

        private bool OnNextLine(TextChunkInfo firstChunk, TextChunkInfo lastChunk)
        {
            //TODO: Account for lines not being exactly at the same height
            return _lineHeights.IndexOf(firstChunk.LineHeight) == _lineHeights.IndexOf(lastChunk.LineHeight) + 1;
        }

        protected class TextChunkInfo : TextChunk
        {
            public TextChunkInfo(TextRenderInfo info)
                : this(GetSegment(info).GetStartPoint(), GetSegment(info).GetEndPoint(), info) { }

            private TextChunkInfo(Vector startLocation, Vector endLocation, TextRenderInfo info)
                : base(info.GetText(), startLocation, endLocation, info.GetSingleSpaceWidth())
            {
                RenderInfo = info;
                StartLocation = startLocation;
                EndLocation = endLocation;
            }

            public new Vector StartLocation { get; }
            public new Vector EndLocation { get; }
            public new float CharSpaceWidth => RenderInfo.GetSingleSpaceWidth();
            public TextRenderInfo RenderInfo { get; }

            public float LineHeight => GetSegment(RenderInfo).GetBoundingRectange().Y +
                                       GetSegment(RenderInfo).GetBoundingRectange().Height;

        }
    }
}
