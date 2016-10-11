using System;
using System.Diagnostics;
using System.Text;
using HtmlDiff;
using System.IO;
using System.Timers;
using System.Collections.Generic;
using System.Net.Mail;
using System.IO.Compression;

namespace Jarvis
{
  public class Runner
  {
    public RunResult Run(Assignment homework)
    {
      RunResult result = new RunResult(homework);

      // Style check
      Logger.Info("Running style check on {0} {1}", homework.StudentId, homework.HomeworkId);
      result.StyleMessage = StyleCheck(homework);

      // Not ready for this yet
      //result.JarvisStyleMessage = JarvisStyleCheck(homework);

      // Compile
      Logger.Info("Compiling {0} {1}", homework.StudentId, homework.HomeworkId);
      result.CompileMessage = Compile(homework);

      // Run tests
      if (result.CompileMessage == "Success!!")
      {
        Logger.Info("Running {0} {1}", homework.StudentId, homework.HomeworkId);
        result.OutputMessage = RunAllTestCases(homework, result);

        // Delete binary
        File.Delete(homework.Path + homework.StudentId);
      }
      else
      {
        result.OutputMessage = "<p>Didn't compile... :(</p>";
      }

      // Write result into results file, writes a new entry for each run
      RecordResult(homework, result);

      return result;
    }

    private void RecordResult(Assignment homework, RunResult result)
    {
      string timestamp = DateTime.Now.ToString();

      using (StreamWriter writer = new StreamWriter(homework.Path + "results.txt", true))
      {
        writer.WriteLine(timestamp + " " + homework.StudentId + " " + result.Grade); 
        writer.Flush();
        writer.Close();
      }
    }

    private string JarvisStyleCheck(Assignment homework)
    {
      StyleExecutor executor = new StyleExecutor();

      string errors = executor.Run(homework.FullPath);

      return Utilities.ToHtmlEncoding(errors);
    }

    private string StyleCheck (Assignment homework)
    {
      string result = string.Empty;
      using (Process p = new Process())
      {
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;

        string styleExe = Jarvis.Config.AppSettings.Settings["styleExe"].Value;
        p.StartInfo.FileName = styleExe;
        p.StartInfo.Arguments = Jarvis.Config.AppSettings.Settings["styleExemptions"].Value + " " + homework.FullPath;

        Logger.Trace("Style checking with {0} and arguments {1}", styleExe, p.StartInfo.Arguments);

        p.Start();
        Jarvis.StudentProcesses.Add(p.Id);

        result = p.StandardError.ReadToEnd();
        result = result.Replace(homework.Path, "");
        result = Utilities.ToHtmlEncoding(result);
        p.WaitForExit();

        p.Close();
      }

      return result;
    }

    private string Compile(Assignment homework)
    {
      string result = "";
      using (Process p = new Process())
      {
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;

        p.StartInfo.FileName = "g++";
        p.StartInfo.Arguments = "-DJARVIS -std=c++11 -Werror " + homework.FullPath + " -o" + homework.Path + homework.StudentId;
        p.Start();

        Jarvis.StudentProcesses.Add(p.Id);

        result = p.StandardError.ReadToEnd();
        result = result.Replace(homework.Path, "");
        result = Utilities.ToHtmlEncoding(result);

        p.WaitForExit();

        p.Close();
      }
      Logger.Trace("Compile result: {0}", result);

      return (!string.IsNullOrEmpty(result)) ? result : "Success!!";
    }

    private string RunAllTestCases(Assignment homework, RunResult grade)
    {
      string testsPath = string.Format("{0}/courses/{1}/tests/hw{2}/", Jarvis.Config.AppSettings.Settings["workingDir"].Value, homework.Course, homework.HomeworkId); 

      Logger.Trace("Running tests as configured in {0}", testsPath);
      StringBuilder result = new StringBuilder();
      int passingTestCases = 0;

      if (File.Exists(testsPath + "config.xml"))
      {
        List<TestCase> tests = Utilities.ReadTestCases(testsPath + "config.xml");

        foreach (TestCase test in tests)
        {
          Logger.Info("Running test case {0}", test.Id);
          string stdInput = string.Empty;

          // clear out any previously created input/output files
          DirectoryInfo dir = new DirectoryInfo(homework.Path);
          foreach (FileInfo file in dir.GetFiles())
          {
            if (!file.Name.Contains(homework.StudentId) && !file.Name.Equals("results.txt"))
            {
              file.Delete(); 
            }
          }

          // check for std input file
          if (!string.IsNullOrEmpty(test.StdInputFile))
          {
            stdInput = testsPath + test.StdInputFile;
          }

          // check for file input files
          foreach (InputFile filein in test.FileInputFiles)
          {
            File.Copy(testsPath + filein.CourseFile, homework.Path + filein.StudentFile, true);
          }

          // Execute the program
          test.StdOutText = ExecuteProgram(homework, stdInput);

          result.AppendLine(test.GetResults(homework.Path, testsPath));

          if (test.Passed)
          {
            passingTestCases++;
          }
        }

        grade.OutputPercentage = passingTestCases / (double)tests.Count;
      }
      else
      {
        result.Append("<p>Sir, I cannot find any test case configurations for this assignment. Perhaps the instructor hasn't set it up yet?<p>");
      }

      return result.ToString();
    }

    private string ExecuteProgram(Assignment homework, string inputFile)
    {      
      string output = string.Empty;
      using (Process executionProcess = new Process())
      {
        executionProcess.StartInfo.WorkingDirectory = homework.Path;
        executionProcess.StartInfo.UseShellExecute = false;
        executionProcess.StartInfo.RedirectStandardOutput = true;
        executionProcess.StartInfo.RedirectStandardError = true;
        executionProcess.StartInfo.RedirectStandardInput = true;

        if (!File.Exists(homework.Path + homework.StudentId))
        {
          Logger.Fatal("Executable " + homework.Path + homework.StudentId + " did not exist!!");
        }

        executionProcess.StartInfo.FileName = homework.Path + homework.StudentId;
        executionProcess.Start();

        Jarvis.StudentProcesses.Add(executionProcess.Id);


        if (File.Exists(inputFile))
        {
          StreamReader reader = new StreamReader(inputFile);

          while (!reader.EndOfStream)
          {
            executionProcess.StandardInput.WriteLine(reader.ReadLine());
          }
        }

        executionProcess.WaitForExit(10000);

        if (executionProcess.HasExited)
        {
          output = executionProcess.StandardOutput.ReadToEnd();
        }
        else
        {
          executionProcess.Kill();
          output = "Sir, the program became unresponsive, either due to an infinite loop or waiting for input.";
        }

        executionProcess.Close();
      }
      
      return output;
    }
  }
}

