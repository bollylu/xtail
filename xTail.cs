using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using BLTools;
using BLTools.Debugging;
using System.Diagnostics;

namespace xtail {
  /// <summary>
  /// Display the content of a text file in pseudo real-time as data are added inside.
  /// The display can be filtered by line.
  /// It can be displayed in full line or columns, eventually formatted
  /// </summary>

  class xTail {

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args) {

      string TempStr;
      long EofPosition = 0;
      long CurrentPosition = 0;
      BinaryReader oReader = null;
      bool FirstPass = true;
      FileStream fs = null;
      BufferedStream bfs = null;
      //bool FilterActive = false;
      bool IsUnix = false;

      int RefreshFrequency;
      int BackLines;

      string FileName;
      char[] aSeparators = new char[] { '\t', ' ' };

      TColInfoCollection ColumnList = new TColInfoCollection();

      SplitArgs oArgs = new SplitArgs(args);

      #region Parameters

      if (oArgs.Count == 0 || oArgs.IsDefined("help")) {
        Usage();
        Environment.Exit(1);
      }

      if (oArgs.IsDefined("debug")) {
        TraceFactory.AddTraceConsole();
      }

      #region filter
      string Filter = oArgs.GetValue<string>("filter", "");
      Regex xFilter = null;
      try {
        xFilter = new Regex(oArgs.GetValue<string>("xfilter", ""));
      } catch (Exception ex) {
        Console.WriteLine("Error in regular expression: {0}", ex.Message);
        Environment.Exit(1);
      }
      //FilterActive = oArgs.IsDefined("filter") || oArgs.IsDefined("xfilter");
      #endregion

      IsUnix = oArgs.IsDefined("unix");

      RefreshFrequency = oArgs.IsDefined("freq") ? Math.Min(Math.Max(oArgs.GetValue<int>("freq", 0), 250), 5000) : 1000;
      BackLines = oArgs.IsDefined("back") ? Math.Min(Math.Max(oArgs.GetValue<int>("back", 0), 1), 1000) : 15;

      if (oArgs.IsDefined("filter") && oArgs.IsDefined("xfilter")) {
        Usage();
        Environment.Exit(1);
      }

      FileName = oArgs.GetValue<string>("file", "");
      Encoding EncodingValue;

      switch (oArgs.GetValue<string>("encoding", "").ToLower()) {
        case "ansi":
          EncodingValue = Encoding.Default;
          break;
        case "ascii":
          EncodingValue = Encoding.ASCII;
          break;
        case "unicode":
          EncodingValue = Encoding.Unicode;
          break;
        case "utf8":
        case "utf-8":
          EncodingValue = Encoding.UTF8;
          break;
        default:
          EncodingValue = Encoding.Default;
          break;
      }

      #region folder
      try {
        if (oArgs.IsDefined("folder")) {
          if (oArgs.IsDefined("pattern")) {
            Console.WriteLine("Searching for last written filename of type {0} in folder {1}", oArgs.GetValue<string>("pattern", ""), oArgs.GetValue<string>("folder", ""));
            DirectoryInfo oDirectoryInfo = new DirectoryInfo(oArgs.GetValue<string>("folder", ""));
            FileInfo[] aFileInfo = oDirectoryInfo.GetFiles(oArgs.GetValue<string>("pattern", ""));
            Console.WriteLine(" -> found {0} files to scan", aFileInfo.Length);
            Array.Sort<FileInfo>(aFileInfo, (f1, f2) => { int CompResult; if ((CompResult = DateTime.Compare(f1.LastWriteTime, f2.LastWriteTime)) == 0) return string.Compare(f1.Name, f2.Name); else return CompResult; });
            FileName = aFileInfo[aFileInfo.Length - 1].FullName;
            Console.WriteLine(" -> found file : {0}", FileName);
          } else {
            Console.WriteLine("[folder] parameter cannot be used without pattern. Please specify.");
          }
        }
      } catch (Exception ex) {
        Console.WriteLine("Error processing [folder] and [pattern] parameters : {0}", ex.Message);
      }
      #endregion

      if (FileName == "") {
        Usage();
        Environment.Exit(1);
      }

      Console.WriteLine("Working with {0}", FileName);

      #region Columns
      if (oArgs.IsDefined("columns")) {
        if (oArgs.IsDefined("separator")) {
          switch (oArgs.GetValue<string>("separator", "").ToLower()) {
            case "tab":
              aSeparators = new char[] { '\t' };
              break;
            case "space":
              aSeparators = new char[] { ' ' };
              break;
            case "pipe":
              aSeparators = new char[] { '|' };
              break;
            default:
              aSeparators = oArgs.GetValue<string>("separator", "").ToCharArray(0, 1);
              break;
          }
        }
        foreach (string sColumn in oArgs.GetValue<string>("columns", "").Split(new char[] { ';' })) {
          string[] aItem = sColumn.Split(new char[] { ':' });
          try {
            ColumnList.Add(new TColInfo(aItem[0], Int32.Parse(aItem[1]), Boolean.Parse(aItem[2])));
          } catch { }
        }
      }
      #endregion
      #endregion Parameters

      #region Main process
      Trace.WriteLine("Main process");
      while (true) {
        try {
          fs = File.Open(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
          bfs = new BufferedStream(fs);
          oReader = new BinaryReader(bfs, EncodingValue);
        } catch (Exception ex) {
          Console.WriteLine("Error opening file : {0}", ex.Message);
          Environment.Exit(1);
        }

        #region Set EOF value, CurrentPosition and BaseStream Position
        if (FirstPass == true) {
          FirstPass = false;
          SetPosAtLine(oReader, BackLines, IsUnix);
          CurrentPosition = oReader.BaseStream.Position;
        } else {
          oReader.BaseStream.Position = CurrentPosition;
        }

        EofPosition = FindEofPos(oReader);
        #endregion Set EOF value, CurrentPosition and BaseStream Position

        while (CurrentPosition < EofPosition) {
          TempStr = GetNextLine(oReader, EofPosition);
          CurrentPosition = oReader.BaseStream.Position;

          if (oArgs.IsDefined("filter") || oArgs.IsDefined("xfilter")) {
            if ((oArgs.IsDefined("filter") && (TempStr.IndexOf(Filter) != -1))) {
              Output(TempStr, ColumnList, aSeparators);
            }

            if (oArgs.IsDefined("xfilter") && xFilter.IsMatch(TempStr)) {
              Output(TempStr, ColumnList, aSeparators);
            }
          } else {
            Output(TempStr, ColumnList, aSeparators);
          }
        }

        #region Closing
        oReader.Close();
        bfs.Close();
        fs.Close();
        #endregion Closing
        Thread.Sleep(RefreshFrequency);

      } // while
      #endregion Main process

    } // Main

    /// <summary>
    /// Obtains the next line of the file
    /// </summary>
    /// <param name="oReader">The binary reader used to access the file</param>
    /// <param name="EofPos">The physical or virtual EOF position, dependding if the file is zero-filled</param>
    /// <returns>The next line of the file</returns></returns>
    static string GetNextLine(BinaryReader reader, long EofPos) {
      const int CHR_LF = 10;
      const int CHR_CR = 13;
      const int CHR_EOF = 26; // ctrl-Z
      const int CHR_NULL = 0;
      StringBuilder TempStr = new StringBuilder();
      int NextChar;
      bool LineCompleted = false;

      try {
        NextChar = CHR_NULL;
        do {
          if (reader.BaseStream.Position < EofPos) {
            NextChar = reader.Read();
            if (NextChar == CHR_CR && reader.BaseStream.Position < EofPos && reader.PeekChar() == CHR_LF) {
              NextChar = reader.Read();
              LineCompleted = true;
            } else if (NextChar == CHR_LF) {
              LineCompleted = true;
            } else if (NextChar != CHR_CR && NextChar != CHR_EOF && NextChar != CHR_NULL) {
              TempStr.Append((char)NextChar);
              LineCompleted = false;
            }
          }
        } while ((reader.BaseStream.Position < EofPos) && (!LineCompleted));
      } catch (Exception ex) {
        Console.WriteLine("EOF reached : {0}", ex.Message);
        Console.WriteLine("EofPos=" + EofPos.ToString() + "  oReader.BaseStream.Length=" + reader.BaseStream.Length.ToString());
      }

      return TempStr.ToString();
    }

    /// <summary>
    /// Set the position of the file-pointer back "n" lines in the file, starting at the end.
    /// </summary>
    /// <param name="reader">The binary reader used to access the file</param>
    /// <param name="lineBack">The number of lines to go back</param>
    /// <param name="unixEOL">Lines are terminated by LF when true, otherwise CRLF</param>
    static void SetPosAtLine(BinaryReader reader, int lineBack, bool unixEOL) {
      const int CHR_LF = 10; // LF
      const int CHR_CR = 13;
      int LineCount = 0;
      bool PreviousWasLF = false;

      Console.Write("Rewinding {0} lines back : ", lineBack);

      reader.BaseStream.Position = FindEofPos(reader);

      while ((reader.BaseStream.Position > 0) && (LineCount <= lineBack)) {

        reader.BaseStream.Position--;

        if (unixEOL) {
          if (reader.PeekChar() == CHR_LF) {
            LineCount++;
            Console.Write(".");
          }
        } else {
          if (reader.PeekChar() == CHR_LF && PreviousWasLF) {
            PreviousWasLF = true;
            LineCount++;
            Console.Write(".");

          } else if (reader.PeekChar() == CHR_LF) {
            PreviousWasLF = true;

          } else if (reader.PeekChar() == CHR_CR && PreviousWasLF) {
            PreviousWasLF = false;
            LineCount++;
            Console.Write(".");

          } else {
            PreviousWasLF = false;

          }
        }
      }

      if (reader.BaseStream.Position > 0) {
        reader.BaseStream.Position++;
      }
      Console.WriteLine();
      Console.WriteLine("Start position reached.");
      return;
    }

    /// <summary>
    /// Find the virtual EOF position into the file (useful for zero-filled files)
    /// </summary>
    /// <param name="oReader">The binary reader used to access the file</param>
    /// <returns>The EOF position</returns>
    static long FindEofPos(BinaryReader reader) {
      // test for empty file
      if (reader.BaseStream.Length == 0) {
        return 0;
      }

      long CurrentPosition = reader.BaseStream.Position;
      long RetVal = 0;
      const int CHR_NULL = 0;

      reader.BaseStream.Position = reader.BaseStream.Length;
      if (reader.PeekChar() != CHR_NULL) {
        // No NULL at the end-of-file
        reader.BaseStream.Position = CurrentPosition;
        return reader.BaseStream.Length;
      }

      // read backward into the file in 256 bytes block while in '0' chars
      while (reader.PeekChar() == CHR_NULL) {
        reader.BaseStream.Position -= Math.Min(reader.BaseStream.Position, 256);
      }

      // read forward into the last block while in non '0' chars
      while ((reader.BaseStream.Position < reader.BaseStream.Length) && (reader.PeekChar() != CHR_NULL)) {
        reader.BaseStream.Position++;
      }

      RetVal = reader.BaseStream.Position;
      reader.BaseStream.Position = CurrentPosition;
      return RetVal;
    }

    /// <summary>
    /// Display the selected line in either line or columns mode, and depending on filters
    /// </summary>
    /// <param name="sTemp">The line to be displayed</param>
    /// <param name="aColumns">The columns list and format</param>
    /// <param name="aSeparators">The data separator</param>
    static void Output(string sTemp, TColInfoCollection columnList, char[] aSeparators) {
      string[] aTemp;
      int iColsToBeDisplayed;

      if (columnList.Count > 0) {
        aTemp = sTemp.Split(aSeparators);
        iColsToBeDisplayed = Math.Min(columnList.Count, aTemp.Length);
        for (int i = 0; i < iColsToBeDisplayed; i++) {
          TColInfo oItem = columnList[i];
          if (oItem.Enabled) {
            if (oItem.Length != 0) {
              Console.Write("{0," + oItem.Length.ToString() + "}|", aTemp[i].Trim());
            } else {
              Console.Write("{0}\t", aTemp[i]);
            }
          }
        }
        Console.WriteLine();
      } else {
        Console.WriteLine(sTemp);
      }
      return;
    }
    /// <summary>
    /// Display Usage
    /// </summary>
    static void Usage() {
      Console.WriteLine("xtail v3.3 - (c) 2007-2013 Luc Bolly - lbolly@hotmail.com");
      Console.WriteLine("Usage: xtail /help");
      Console.WriteLine("       xtail /file=filename | /folder=path /pattern=pattern file");
      Console.WriteLine("             [/filter=\"string\"|/xfilter=\"regex\"]");
      Console.WriteLine("             [/freq=refresh frequency in msec] [/back=number of lines]");
      Console.WriteLine("             [/columns=\"colname\":colsize:enabled;...]");
      Console.WriteLine("             [/separator=colsep|\"tab\"|\"space\"|\"pipe\"]");
      Console.WriteLine("             [/encoding=ansi|unicode|ascii|utf8 (default=ansi)]");
      Console.WriteLine("             [/unix (use unix LF instead of windows CRLF)]");
      Console.WriteLine("");
      Console.WriteLine("Example 1: xtail /file=c:\\mylog.txt");
      Console.WriteLine("  Display the content of mylog.txt");
      Console.WriteLine("Example 2: xtail /folder=\\\\myserver\\myshare\\myfolder /pattern=*.log /filter=\"Connection\"");
      Console.WriteLine("  Display the contents of the last written file with pattern *.log in the folder myfolder. Only");
      Console.WriteLine("  lines containing the word \"Connection\" will be displayed");
      Console.WriteLine("Example 3: xtail /file=c:\\mylog.txt /xfilter=\"(Udp\\t.*\\t500)|(Tcp\\t.*\\t445)\"");
      Console.WriteLine("  Display the content of mylog.txt if the regular expression matches");
      Console.WriteLine("Example 4: xtail /file=c:\\mylog.txt /columns=date::false;time:9;\"ip address\":-16;comment /separator=tab");
      Console.WriteLine("  Display the content of c:\\mylog.txt in columns (original data are separated by tabs):");
      Console.WriteLine("    first column date is not displayed;");
      Console.WriteLine("    second column time is right aligned in 9 positions;");
      Console.WriteLine("    third column ip address is left aligned in 16 positions;");
      Console.WriteLine("    fourth column is display left aligned as read.");
      return;
    }


  } // xTail
} // xtail
