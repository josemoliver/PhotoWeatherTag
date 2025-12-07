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
        //* Title:     Photo WeatherTag
        //* Author:    José Oliver-Didier
        //* Purpose:   Tags images with weather data from a CSV history file using Exiftool.
        //* History:
        //*          - October  2018: Created initial version.
        //*          - December 2024: Code cleanup, minor improvements.
        //*          - December 2025: Updated, code cleanup, added HEIC and JPEG XL support.
        //*
        //* License:   MIT License
        //* Ref:
        //* https://github.com/josemoliver/WeatherTag
        //* https://jmoliver.wordpress.com/2018/07/07/capturing-the-moment-and-the-ambient-weather-information-in-photos/
        //**

        static void Main(string[] args)
        {
            // Parse command line parameters
            // Get current directory as the working folder for images and CSV file
            string activeFilePath = Directory.GetCurrentDirectory();
            
            // Check if -write flag is present to enable writing weather data to files
            bool writeToFile = args.Any(a => a.Equals("-write", StringComparison.OrdinalIgnoreCase));
            
            // Default time threshold for matching weather readings to photos (in minutes)
            int thresholdMinutes = 30;

            // Parse optional -threshold parameter to override default matching threshold
            // Format: -threshold=N where N is the number of minutes
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

            // Verify that exiftool.exe is available in the system PATH or current directory
            // Exiftool is required for reading and writing EXIF metadata
            double exiftoolVersion = CheckExiftool();
            if (exiftoolVersion == 0)
            {
                Console.WriteLine("Exiftool not found! Photo WeatherTag needs exiftool in order to work properly.");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("Exiftool version " + exiftoolVersion);
            }

            // Discover all supported image files in the current directory
            // Supported formats: JPEG (.jpg, .jpeg), JPEG XL (.jxl), and HEIC (.heic)
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

            // Load weather history from CSV file
            // Expected format: Date, Time, Temperature(°C), Humidity(%), Pressure(hPa)
            // Examples of valid rows:
            // 11/17/2025,4:44 AM,24.0 °C,88 %,"1,012.02 hPa"   -> With units
            // 11/17/2025,4:44 AM,24.0,88,1012.02               -> Without units
            // 11/17/2025,4:44 AM,24.0 °C,88,1012.02 hPa        -> Mixed (clean numeric values)
            // 11-17-2025,04:44,24.0 °C,88 %,1012.02            -> Different date/time formats, date format (MM-DD-YYYY) and 24-hour time
            // 11/17/2025,4:44 AM,24.0°C,88%,1012.02hPa         -> No spaces before units
            
            string weatherHistoryFile = Path.Combine(activeFilePath, "weatherhistory.csv");
            List<WeatherReading> readings = ImportWeatherHistory(weatherHistoryFile);

            // Initialize counters to track processing results for final summary
            int matchedCount = 0;
            int writtenCount = 0;
            int noPhotoDateCount = 0;
            int noReadingWithinThresholdCount = 0;

            // Process each image file found in the directory
            foreach (var file in imageFiles)
            {
                // Extract the CreateDate from the image's EXIF metadata
                DateTime? photoDate = null;
                try { photoDate = GetFileDate(file); }
                catch { photoDate = null; }

                // Find the closest weather reading to the photo's timestamp
                WeatherReading nearest = null;
                double deltaMinutes = double.MaxValue;

                if (photoDate.HasValue)
                {
                    // Linear search through all weather readings to find the nearest one
                    // Calculate time difference in minutes for each reading
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

                // Create result object with matched weather data and status
                // Status can be: "Matched", "NoPhotoDate", or "NoReadingWithinThreshold"
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

                // Display the result for this image to console
                Console.WriteLine($"{Path.GetFileName(file)} | {result.Status} | " +
                    $"Temp={result.AmbientTemperature?.ToString() ?? "--"} °C, " +
                    $"Hum={result.Humidity?.ToString() ?? "--"} %, " +
                    $"Press={result.Pressure?.ToString() ?? "--"} hPa");

                // Update status counters for summary report
                if (result.Status == "Matched")
                    matchedCount++;
                else if (result.Status == "NoPhotoDate")
                    noPhotoDateCount++;
                else if (result.Status == "NoReadingWithinThreshold")
                    noReadingWithinThresholdCount++;

                // Write weather data to file if -write flag is set and a match was found
                if (writeToFile && nearest != null && deltaMinutes <= thresholdMinutes)
                {
                    WriteFileInfo(file, nearest);
                    writtenCount++;
                }
            }

            // Display comprehensive summary of all operations performed
            // Shows statistics for: total files, matches, errors, and files written
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("SUMMARY");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"Total images processed: {imageFiles.Length}");
            Console.WriteLine($"Weather readings loaded: {readings.Count}");
            Console.WriteLine($"Matched (within {thresholdMinutes} min threshold): {matchedCount}");
            Console.WriteLine($"No photo date found: {noPhotoDateCount}");
            Console.WriteLine($"No reading within threshold: {noReadingWithinThresholdCount}");
            if (writeToFile)
            {
                Console.WriteLine($"Files written with weather data: {writtenCount}");
            }
            else
            {
                Console.WriteLine("\nNote: Running in preview mode. Use -write to save weather data to files.");
            }
            Console.WriteLine(new string('=', 60));
        }

        /// <summary>
        /// Imports weather history from a CSV file
        /// Expected format: Date, Time, Temperature, Humidity, Pressure
        /// Automatically detects and skips header rows
        /// </summary>
        static List<WeatherReading> ImportWeatherHistory(string csvPath)
        {
            var readings = new List<WeatherReading>();

            // Verify CSV file exists before attempting to read
            if (!File.Exists(csvPath))
            {
                Console.WriteLine($"{csvPath} not found.");
                Environment.Exit(0);
            }

            using (var csv = new CsvReader(new StreamReader(csvPath), hasHeaders: false))
            {
                bool isFirstRow = true;

                while (csv.ReadNextRecord())
                {
                    // Ensure row has at least 5 columns (Date, Time, Temp, Humidity, Pressure)
                    if (csv.FieldCount < 5)
                        continue;

                    string dateField = csv[0]?.Trim();
                    string timeField = csv[1]?.Trim();

                    // Auto-detect and skip header row on first iteration
                    // If the first row's date/time fields cannot be parsed, assume it's a header
                    if (isFirstRow)
                    {
                        isFirstRow = false;
                        if (!DateTime.TryParse(dateField + " " + timeField, out DateTime _))
                        {
                            Console.WriteLine("Header row detected and skipped.");
                            continue;
                        }
                    }

                    // Parse date and time into a DateTime object
                    if (!DateTime.TryParse(dateField + " " + timeField, out DateTime dt))
                        continue;

                    // Parse weather values with validation ranges
                    // Temperature: -100°C to 150°C, Humidity: 0% to 100%, Pressure: 800 to 1100 hPa
                    double? temp = TryParseDouble(csv[2], -100, 150);
                    double? hum = TryParseDouble(csv[3], 0, 100);
                    double? press = TryParseDouble(csv[4], 800, 1100);

                    readings.Add(new WeatherReading(dt, temp, hum, press));
                }
            }

            // Sort readings chronologically for efficient searching
            return readings.OrderBy(r => r.ReadingDate).ToList();
        }

        /// <summary>
        /// Safely parses a string to a double with range validation
        /// Returns null if parsing fails or value is out of range
        /// Removes non-numeric characters (except digits, decimal point, and minus sign)
        /// </summary>
        static double? TryParseDouble(string input, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            try
            {
                // Remove any non-numeric characters (e.g., units like °C, %, hPa)
                var cleaned = RemoveNonNumeric(input);
                if (string.IsNullOrWhiteSpace(cleaned))
                    return null;

                // Parse using invariant culture to handle decimal separators consistently
                double val = double.Parse(cleaned, System.Globalization.CultureInfo.InvariantCulture);
                
                // Validate that the value is within the expected range
                if (val < min || val > max) return null;
                return val;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Removes all non-numeric characters except digits, decimal point, and minus sign
        /// Used to clean weather data that may contain unit symbols
        /// </summary>
        static string RemoveNonNumeric(string input) => Regex.Replace(input, @"[^0-9\.\-]", "");

        /// <summary>
        /// Extracts the CreateDate from an image file's EXIF metadata using exiftool
        /// Returns the date/time when the photo was taken
        /// </summary>
        public static DateTime GetFileDate(string file)
        {
            // Execute exiftool to extract CreateDate field from image EXIF data
            // Uses -mwg option for Metadata Working Group compatibility
            // Returns JSON format for easy parsing
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.Arguments = $"\"{file}\" -CreateDate -mwg -json";
            p.StartInfo.FileName = "exiftool.exe";
            p.Start();

            string json = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            // Parse JSON response and extract CreateDate field
            var exifResponse = JsonConvert.DeserializeObject<List<ExifToolJSON>>(json);
            string raw = exifResponse[0].CreateDate.Trim();
            
            // Parse EXIF date format (yyyy:MM:dd HH:mm:ss) into DateTime object
            return DateTime.ParseExact(raw, "yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Checks if exiftool is available and returns its version number
        /// Returns 0 if exiftool is not found or cannot be executed
        /// </summary>
        public static double CheckExiftool()
        {
            try
            {
                // Execute exiftool with -ver flag to get version number
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
            catch { return 0; }  // Return 0 if exiftool is not found
        }

        /// <summary>
        /// Writes weather data to an image file's EXIF metadata using exiftool
        /// Only writes fields that have values (temperature, humidity, pressure)
        /// Overwrites the original file without creating a backup
        /// </summary>
        public static void WriteFileInfo(string file, WeatherReading reading)
        {
            // Build exiftool arguments for available weather data fields
            var args = new List<string>();
            if (reading.AmbientTemperature.HasValue) args.Add($"-AmbientTemperature={reading.AmbientTemperature}");
            if (reading.Humidity.HasValue) args.Add($"-Humidity={reading.Humidity}");
            if (reading.Pressure.HasValue) args.Add($"-Pressure={reading.Pressure}");
            
            // Use -overwrite_original to modify file in place without backup
            args.Add("-overwrite_original");

            // Execute exiftool to write weather data to EXIF metadata
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.Arguments = $"\"{file}\" {string.Join(" ", args)}";
            p.StartInfo.FileName = "exiftool.exe";
            p.Start();
            p.WaitForExit();
        }

        /// <summary>
        /// Data structure for deserializing exiftool JSON output
        /// Matches the JSON format returned by exiftool -json command
        /// </summary>
        public class ExifToolJSON
        {
            public string SourceFile { get; set; }
            public string CreateDate { get; set; }
        }

        /// <summary>
        /// Represents a single weather reading with timestamp and measurements
        /// Temperature in Celsius, Humidity in percentage, Pressure in hectopascals (hPa)
        /// All weather values are nullable to handle missing or invalid data
        /// </summary>
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
            public double? AmbientTemperature { get; set; }  // Temperature in °C
            public double? Humidity { get; set; }             // Relative humidity in %
            public double? Pressure { get; set; }             // Atmospheric pressure in hPa
        }
    }
}
