using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using HtmlAgilityPack;

namespace BPArchiveScraper
{
    enum Mode
    {
        Metadata = 0,
        Sentiment = 1,
    }

    class Scraper
    {
        // If offline, skip the download step
        public static bool _isOffline = false;

        // Whether or not to download the articles into files
        public static bool _downloadArticles = false;

        // Depending on the mode, construct a corresponding summary file
        public static Mode _scraperMode = Mode.Metadata;

        // 2010 (9901 - 12658)
        public static int _articleIdStart = 9901;
        public static int _articleIdEnd = 12658;

        // BP id of article to analyze
        // Ids tested: 9901 - 10000, 11839 - 11849

        static void Main(string[] args)
        {
            // Map of article Ids to article maps (the latter contain the data and metadata)
            var idToArticleMap = new Dictionary<int, Dictionary<string, string>>();

            // Build the article map
            for (int i = _articleIdStart; i <= _articleIdEnd; i++)
            {
                Console.WriteLine(i);
                idToArticleMap.Add(i, GetArticleInfo(i, _scraperMode));
            }

            // Use the idToArticleMap to output a summary to a .csv file

            // Don't overwrite the file if it exists.
            if (File.Exists(GetFullCsvFilePath(_scraperMode, _articleIdStart, _articleIdEnd)))
                return;

            // Create the summary file.
            using (FileStream fs = File.Create(GetFullCsvFilePath(_scraperMode, _articleIdStart, _articleIdEnd)))
            {
                // Write the header
                string csvFormat, headerLine;
                byte[] headerInfo;
                switch (_scraperMode)
                {
                    case Mode.Metadata:
                        csvFormat = "{0},{1},{2},{3}\n";
                        headerLine = String.Format(csvFormat, "Id", "Date", "Title", "Author");
                        headerInfo = new UTF8Encoding(true).GetBytes(headerLine);
                        fs.Write(headerInfo, 0, headerInfo.Length);
                        break;
                    case Mode.Sentiment:
                        csvFormat = "{0},{1}\n";
                        headerLine = String.Format(csvFormat, "Id", "Sentiment");
                        headerInfo = new UTF8Encoding(true).GetBytes(headerLine);
                        fs.Write(headerInfo, 0, headerInfo.Length);
                        break;
                    default:
                        Console.WriteLine("Unexpected execution mode.");
                        return;
                }

                // Write the body
                foreach (var kvp in idToArticleMap)
                {
                    Dictionary<string, string> articleMap;
                    if (idToArticleMap.TryGetValue(kvp.Key, out articleMap))
                    {
                        string bodyLine = String.Empty;
                        switch (_scraperMode)
                        {
                            case Mode.Metadata:
                                if (articleMap.ContainsKey("date") && articleMap.ContainsKey("title") && articleMap.ContainsKey("author"))
                                    bodyLine = String.Format(csvFormat, kvp.Key, articleMap["date"], articleMap["title"], articleMap["author"]);
                                break;
                            case Mode.Sentiment:
                                if (articleMap.ContainsKey("sentiment"))
                                    bodyLine = String.Format(csvFormat, kvp.Key, articleMap["sentiment"]);
                                break;
                            default:
                                Console.WriteLine("Unexpected execution mode.");
                                return;
                        }

                        if (bodyLine != String.Empty)
                        {
                            byte[] bodyInfo = new UTF8Encoding(true).GetBytes(bodyLine);
                            fs.Write(bodyInfo, 0, bodyInfo.Length);
                        }
                    }
                }
            }

            Console.WriteLine("EOP");
        }

        static Dictionary<string, string> GetArticleInfo(int articleId, Mode mode)
        {
            var articleMap = new Dictionary<string, string>();

            // If we're offline, just assume that the txt file was already written to ScrapedFiles directory
            if (!_isOffline && mode == Mode.Metadata)
            {
                // Populate the following Keys: date, title, author, article
                // And write the article to ScrapedFiles for sentiment analysis
                articleMap = DownloadArticleAndParseMetadata(articleId);
            }

            if (mode == Mode.Sentiment)
            {
                // Run StanfordNPS on the file, extract sentiment, add it to the map
                var sentiment = ExecuteStanfordNPSOnFileAndExtractSentiment(articleId);
                articleMap["sentiment"] = sentiment.ToString();
            }

            return articleMap;
        }

        static Dictionary<string, string> DownloadArticleAndParseMetadata(int articleId)
        {
            var articleMap = new Dictionary<string, string>();

            // Initialize cleaning substitutions
            var cleanSubs = InitializeCleanSubs();

            // Given an HtmlDocument, construct a map containing metadata and article information
            // Map to construct
            var urlPrefix = "http://www.baseballprospectus.com/article.php?articleid=";
            var urlSuffix = "&mode=print&nocache=1494995156";
            HtmlDocument htmlDoc = new HtmlWeb().Load(urlPrefix + articleId + urlSuffix);

            // There should be exactly one article, grab it
            var articleNodes = htmlDoc.DocumentNode.SelectNodes("//*[@class=\"article\"]");
            if (articleNodes != null && articleNodes.Count == 1)
            {
                // Found the article
                var article = articleNodes[0];

                // Header region

                // <div class="tools">...</div>
                // <p class="date">August 25, 2010</p>
                // <h1 class="title">Manufactured Runs</h1>
                // <h2 class="subtitle">Support Group</h2>
                // <p class="author">by <a class="author" href="/author/colin_wyers/">Colin Wyers</a></p>
                // <div style="display: inline;">~~Print/Mail Buttons~~</div>
                // <table width="700" class="freeweek">~~Archives are free box~~</table>
                // <p></p>

                // Remove tools, subtitle, freeweek
                var toolsNodes = article.SelectNodes("//*[@class=\"tools\"]");
                if (toolsNodes != null)
                    foreach (var node in toolsNodes)
                        node.Remove();
                var subtitleNodes = article.SelectNodes("//*[@class=\"subtitle\"]");
                if (subtitleNodes != null)
                    foreach (var node in subtitleNodes)
                        node.Remove();
                var freeweekNodes = article.SelectNodes("//*[@class=\"freeweek\"]");
                if (freeweekNodes != null)
                    foreach (var node in freeweekNodes)
                        node.Remove();

                // Extract and remove date
                var dateNodes = article.SelectNodes("//*[@class=\"date\"]");
                if (dateNodes.Count > 0)
                {
                    var dateNode = dateNodes.First();
                    var dateString = dateNode.InnerText;
                    articleMap["date"] = dateString != null && dateString.Contains(",") ? dateString.Replace(",", "") : dateString;
                    dateNode.Remove();
                }
                else
                    Console.WriteLine("Unexpected date node count: " + dateNodes.Count);

                // Extract and remove title
                var titleNodes = article.SelectNodes("//*[@class=\"title\"]");
                if (titleNodes.Count > 0)
                {
                    var titleNode = titleNodes.First();
                    articleMap["title"] = titleNode.InnerText;
                    titleNode.Remove();
                }
                else
                    Console.WriteLine("Unexpected title node count: " + titleNodes.Count);

                // Extract and remove author
                var authorNodes = article.SelectNodes("//*[@class=\"author\"]");
                if (authorNodes.Count > 0)
                {
                    var authorNode = authorNodes.First();
                    var authorString = authorNode.InnerText;
                    articleMap["author"] = authorString != null && authorString.StartsWith("by ") ? authorString.Substring(3, authorString.Length - 3) : authorNode.InnerText;
                    authorNode.Remove();
                }
                else
                    Console.WriteLine("Unexpected author node count: " + authorNodes.Count);

                // Article region
                if (_downloadArticles)
                {
                    var paragraphNodes = article.Descendants("p");
                    var articleString = "";
                    foreach (var paragraph in paragraphNodes)
                    {
                        var cleanText = MakeClean(paragraph.InnerText, cleanSubs);
                        articleString += cleanText;
                    }
                    articleMap["article"] = articleString;

                    // Write the article to a file and keep track of the metadata
                    System.IO.File.WriteAllLines(GetFullFilePath(articleId), new string[] { articleMap["article"] });
                }
            }
            else
            {
                Console.WriteLine("Unexpected article node count: " + articleNodes?.Count ?? "-1");
            }

            return articleMap;
        }

        static string GetFullFilePath(int articleId, bool isXml = false, bool isStanfordNLPFile = false)
        {
            return @"C:\Code\Sentiment\" + (isStanfordNLPFile ? @"StanfordCoreNLP\" : @"ScrapedFiles\") + articleId + ".txt" + (isXml ? ".xml" : "");
        }

        static string GetFullCsvFilePath(Mode mode, int startId, int endId)
        {
            string fileName;
            switch (mode)
            {
                case Mode.Metadata:
                    fileName = "METADATASUMMARY";
                    break;
                case Mode.Sentiment:
                    fileName = "SENTIMENTSUMMARY";
                    break;
                default:
                    Console.WriteLine("Unexpected execution mode.");
                    fileName = "UNKNOWNSUMMARY";
                    break;
            }
            return @"C:\Code\Sentiment\ScrapedFiles\Summary\" + fileName + "_" + startId + "_" + endId +  ".csv";
        }

        /// <summary>
        /// Constructs and returns a dictionary for use in MakeClean
        /// </summary>
        /// <returns>Dictionary containing xml to human-readable format transformations</returns>
        static Dictionary<string, string> InitializeCleanSubs()
        {
            var cleanSubs = new Dictionary<string, string>();
            cleanSubs["&lsquo;"] = "'"; // Left single quote
            cleanSubs["&rsquo;"] = "'"; // Right single quote
            cleanSubs["&#39;"] = "'"; // Single quote
            cleanSubs["&ldquo;"] = "\""; // Left double quote
            cleanSubs["&rdquo;"] = "\""; // Right double quote
            cleanSubs["&quot;"] = "\""; // Double quote
            cleanSubs["&nbsp;"] = " "; // Space
            cleanSubs["&mdash;"] = ", "; // Dash (interpreted as comma)
            cleanSubs["&amp;"] = "&"; // Ampersand
            cleanSubs["&hellip;"] = "..."; // Ellipses
            cleanSubs["&frac12;"] = ".5"; // 1/2 fraction symbol

            return cleanSubs;
        }

        /// <summary>
        /// Converts text downloaded using HtmlWeb().Load to human-readable format
        /// </summary>
        /// <param name="text">Text to be cleaned</param>
        /// <param name="cleanSubs">Dictionary of transformations on the text</param>
        /// <returns>Clean version of the text string with xml-like constructs replaced</returns>
        static string MakeClean(string text, Dictionary<string, string> cleanSubs)
        {
            foreach (var kvp in cleanSubs) text = text.Replace(kvp.Key, kvp.Value);
            return text;
        }

        /// <summary>
        /// Construct the StanforNPS command to 
        /// </summary>
        /// <param name="filePath">FilePath to run NPS on</param>
        /// <returns>The double representation of the sentiment value output by StanfordNPS on the given file. StanfordNPS evaluates each sentence,
        /// and this function returns the average sentiment of those sentence. TODO: Weight sentences?</returns>
        static double ExecuteStanfordNPSOnFileAndExtractSentiment(int articleId)
        {
            // Construct the StanfordNPS command
            var filePath = GetFullFilePath(articleId);
            var nlpCommand = "java -cp \"*\" -Xmx2g edu.stanford.nlp.pipeline.StanfordCoreNLP -annotators tokenize,ssplit,pos,parse,sentiment -file " + filePath;
            var workingDirectory = "C:\\Code\\Sentiment\\StanfordCoreNLP";

            // Execute it
            ExecuteCommandSync(workingDirectory, nlpCommand);

            // The output is placed in C:\Code\sentiment\StanfordCoreNLP\<articleId>.txt.xml
            // Move it to a better location: C:\Code\sentiment\ScrapedFiles\<articleId>.txt.xml
            var sourceFilePath = GetFullFilePath(articleId, isXml: true, isStanfordNLPFile: true);
            var targetFilePath = GetFullFilePath(articleId, isXml: true);
            MoveFile(sourceFilePath, targetFilePath);

            // Wait for the file to exist (TODO: make MoveFile async and replace this with await Task)
            //while (!File.Exists(targetFilePath))
            //    Thread.Sleep(100); // Still doesn't exist? Wait 0.1s and try again.

            XmlDocument sentimentXmlDoc;

            try
            {
                // Parse the xml file at the targetFile location
                sentimentXmlDoc = new XmlDocument();
                sentimentXmlDoc.Load(targetFilePath);
            }
            catch (XmlException e)
            {
                Console.WriteLine(e.Message);
                return -1.0;
            }


            // Extract the sentiment from the StandformNPS XML output
            return GetSentiment(sentimentXmlDoc);
        }

        /// <summary>
        /// Given output from nps, extract the sentiment value as an enum
        /// </summary>
        /// <param name="npsOutput">Output of StanfordNPS</param>
        /// <returns>Double representation of the sentiment for this npsOutput</returns>
        static double GetSentiment(XmlDocument xmlDoc)
        {
            // Iterate through all the sentences, adding the sentimentValues to a running total
            // Then return the average
            double total = 0.0;
            var sentences = xmlDoc.GetElementsByTagName("sentence");
            for (int i = 0; i < sentences.Count; i++)
            {
                var sentence = sentences[i];

                // Second of third attribute should be SentimentValue
                if (sentence.Attributes.Count != 3 || sentence.Attributes[1].Name != "sentimentValue")
                    continue;

                var sentimentValueString = sentence.Attributes[1].InnerText;

                // TryParse as int
                int sentimentValue;
                if (int.TryParse(sentimentValueString, out sentimentValue))
                    total += sentimentValue;
            }

            return sentences.Count == 0 ? 0.0 : total / sentences.Count;
        }

        /// <span class="code-SummaryComment"><summary></span>
        /// Executes a shell command synchronously. Credit: https://www.codeproject.com/Articles/25983/How-to-Execute-a-Command-in-C
        /// <span class="code-SummaryComment"></summary></span>
        /// <span class="code-SummaryComment"><param name="command">string command</param></span>
        /// <span class="code-SummaryComment"><returns>string, as output of the command.</returns></span>
        /// <returns>The string output of the command</returns>
        static void ExecuteCommandSync(string workingDirectory, string command)
        {
            try
            {
                // create the ProcessStartInfo using "cmd" as the program to be run,
                // and "/c " as the parameters.
                // Incidentally, /c tells cmd that we want it to execute the command that follows,
                // and then exit.
                System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo("cmd", "/c " + command);
                procStartInfo.WorkingDirectory = workingDirectory;

                // The following commands are needed to redirect the standard output.
                // This means that it will be redirected to the Process.StandardOutput StreamReader.
                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.UseShellExecute = false;
                // Do not create the black window.
                procStartInfo.CreateNoWindow = false;
                // Now we create a process, assign its ProcessStartInfo and start it (synchronously)
                System.Diagnostics.Process proc = new System.Diagnostics.Process();
                proc.StartInfo = procStartInfo;
                proc.Start();

                // The process completed
            }
            catch (Exception objException)
            {
                // Log the exception
                Console.WriteLine(objException.ToString());
            }
        }

        /// <summary>
        /// Moves a file using System.IO.File. Credit (MSDN): https://msdn.microsoft.com/en-us/library/system.io.file.move(v=vs.110).aspx
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <returns>bool indicating whether or not the move succeeded</returns>
        static bool MoveFile(string sourcePath, string targetPath)
        {
            bool succeeded;
            try
            {
                if (!File.Exists(sourcePath))
                {
                    // This statement ensures that the file is created,
                    // but the handle is not kept.
                    using (FileStream fs = File.Create(sourcePath)) { }
                }

                // Ensure that the target does not exist.
                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                // Move the file.
                File.Move(sourcePath, targetPath);
                Console.WriteLine("{0} was moved to {1}.", sourcePath, targetPath);

                // See if the original exists now, which it shouldn't
                succeeded = !File.Exists(sourcePath);
                if (!succeeded)
                    Console.WriteLine("ERROR: The original file still exists, which is unexpected.");
            }
            catch (Exception e)
            {
                Console.WriteLine("The process failed: {0}", e.ToString());
                succeeded = false;
            }

            return succeeded;
        }
    }
}
