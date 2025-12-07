using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using LumenWorks.Framework.IO.Csv;
using System.Text.RegularExpressions;

namespace WeatherTag
{
    class Program
    {
        //**
        //* Author:    José Oliver-Didier
        //* Refactored: December 2025
        //* Ref: https://github.com/josemoliver/WeatherTag
        //**

        static void Main(string[] args)
        {
            // Parameters
            string activeFilePath = Directory.GetCurrentDirectory();
            bool writeToFile = args.Any(a => a.Equals("-write", StringComparison.OrdinalIgnoreCase));
            int thresholdMinutes = 30;

            // Optional Threshold parameter
            foreach (var arg in args)
            {
                if (arg.StartsWith("-threshold", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = arg.Split('=');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int t))
                    {
                        thresholdMinutes = t;
                    }
                }
            }

            // Check exiftool
            double exiftoolVersion = CheckExiftool();
            if (exiftoolVersion == 0)
            {
                Console.WriteLine("Exiftool not found! WeatherTag needs exiftool in order to work properly.");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("Exiftool version " + exiftoolVersion);
            }

            // Find images
            string[] imageFiles = Directory.GetFiles(activeFilePath, "*.*")
                .Where(file => file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)   //JPEG
                            || file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)   //JPEG
                            || file.EndsWith(".jxl", StringComparison.OrdinalIgnoreCase)    //JPEG XL
                            || file.EndsWith(".heic", StringComparison.OrdinalIgnoreCase))  //HEIC  
                .ToArray();

            if (imageFiles.Length == 0)
            {
                Console.WriteLine("No supported image files found.");
                Environment.Exit(0);
            }

            string weatherHistoryFile = Path.Combine(activeFilePath, "weatherhistory.csv");
            List<WeatherReading> readings = ImportWeatherHistory(weatherHistoryFile);

            // Process each image
            foreach (var file in imageFiles)
            {
                DateTime? photoDate = null;
                try { photoDate = GetFileDate(file); }
                catch { photoDate = null; }

                WeatherReading nearest = null;
                double deltaMinutes = double.MaxValue;

                if (photoDate.HasValue)
                {
                    foreach (var r in readings)
                    {
                        var diff = Math.Abs((photoDate.Value - r.ReadingDate).TotalMinutes);
                        if (diff < deltaMinutes)
                        {
                            deltaMinutes = diff;
                            nearest = r;
                        }
                    }
                }

                var result = new
                {
                    File = file,
                    PhotoDate = photoDate,
                    ReadingDate = nearest?.ReadingDate,
                    AmbientTemperature = nearest?.AmbientTemperature,
                    Humidity = nearest?.Humidity,
                    Pressure = nearest?.Pressure,
                    Status = (nearest != null && deltaMinutes <= thresholdMinutes) ? "Matched" :
                             (photoDate == null ? "NoPhotoDate" : "NoReadingWithinThreshold")
                };

                Console.WriteLine($"{Path.GetFileName(file)} | {result.Status} | " +
                    $"Temp={result.AmbientTemperature?.ToString() ?? "--"} °C, " +
                    $"Hum={result.Humidity?.ToString() ?? "--"} %, " +
                    $"Press={result.Pressure?.ToString() ?? "--"} hPa");

                if (writeToFile && nearest != null && deltaMinutes <= thresholdMinutes)
                {
                    WriteFileInfo(file, nearest);
                }
            }
        }

        static List<WeatherReading> ImportWeatherHistory(string csvPath)
        {
            var readings = new List<WeatherReading>();

            if (!File.Exists(csvPath))
            {
                Console.WriteLine($"{csvPath} not found.");
                Environment.Exit(0);
            }

            using (var csv = new CsvReader(new StreamReader(csvPath), hasHeaders: false))
            {
                while (csv.ReadNextRecord())
                {
                    // Ensure row has expected column count
                    if (csv.FieldCount < 5)
                        continue;

                    string dateField = csv[0]?.Trim();
                    string timeField = csv[1]?.Trim();

                    if (!DateTime.TryParse(dateField + " " + timeField, out DateTime dt))
                        continue;

                    double? temp = TryParseDouble(csv[2], -100, 150);
                    double? hum = TryParseDouble(csv[3], 0, 100);
                    double? press = TryParseDouble(csv[4], 800, 1100);

                    readings.Add(new WeatherReading(dt, temp, hum, press));
                }
            }

            return readings.OrderBy(r => r.ReadingDate).ToList();
        }

        static double? TryParseDouble(string input, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            try
            {
                var cleaned = RemoveNonNumeric(input);
                if (string.IsNullOrWhiteSpace(cleaned))
                    return null;

                double val = double.Parse(cleaned, System.Globalization.CultureInfo.InvariantCulture);
                if (val < min || val > max) return null;
                return val;
            }
            catch
            {
                return null;
            }
        }


        static string RemoveNonNumeric(string input) => Regex.Replace(input, @"[^0-9\.\-]", "");

        public static DateTime GetFileDate(string file)
        {
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.Arguments = $"\"{file}\" -CreateDate -mwg -json";
            p.StartInfo.FileName = "exiftool.exe";
            p.Start();

            string json = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            var exifResponse = JsonConvert.DeserializeObject<List<ExifToolJSON>>(json);
            string raw = exifResponse[0].CreateDate.Trim();
            return DateTime.ParseExact(raw, "yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static double CheckExiftool()
        {
            try
            {
                Process p = new Process();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.Arguments = "-ver";
                p.StartInfo.FileName = "exiftool.exe";
                p.Start();

                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return double.Parse(output);
            }
            catch { return 0; }
        }

        public static void WriteFileInfo(string file, WeatherReading reading)
        {
            var args = new List<string>();
            if (reading.AmbientTemperature.HasValue) args.Add($"-AmbientTemperature={reading.AmbientTemperature}");
            if (reading.Humidity.HasValue) args.Add($"-Humidity={reading.Humidity}");
            if (reading.Pressure.HasValue) args.Add($"-Pressure={reading.Pressure}");
            args.Add("-overwrite_original");

            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.Arguments = $"\"{file}\" {string.Join(" ", args)}";
            p.StartInfo.FileName = "exiftool.exe";
            p.Start();
            p.WaitForExit();
        }

        public class ExifToolJSON
        {
            public string SourceFile { get; set; }
            public string CreateDate { get; set; }
        }

        public class WeatherReading
        {
            public WeatherReading(DateTime readingDate, double? ambientTemperature, double? humidity, double? pressure)
            {
                ReadingDate = readingDate;
                AmbientTemperature = ambientTemperature;
                Humidity = humidity;
                Pressure = pressure;
            }

            public DateTime ReadingDate { get; set; }
            public double? AmbientTemperature { get; set; }
            public double? Humidity { get; set; }
            public double? Pressure { get; set; }
        }
    }
}
