# Photo Weather Tag
Capturing the moment and the weather in images.

![Photo WeatherTag](https://github.com/josemoliver/PhotoWeatherTag/raw/main/blob/PhotoWeatherTag.png "Photo Weather Tag")

## Introduction

Photo Weather Tag is a simple Windows console (Command Line) application for adding weather values to image file EXIF 2.31 Metadata. Using the date and time a photo was taken, the app matches the closest weather reading from a file containing periodic weather readings - weatherhistory.csv.

The idea behind this utility is if you can obtain historical weather information for a location near where the photos were taken this information (Ambient Temperature, Humidity and Pressure) could be saved within the image file metadata.

**Supported Image Formats:**
- JPEG (.jpg, .jpeg)
- JPEG XL (.jxl)
- HEIC (.heic)

Read the blog post: [Capturing the moment and the ambient weather information in photos](https://jmoliver.wordpress.com/2018/07/07/capturing-the-moment-and-the-ambient-weather-information-in-photos/)

## Dependency with ExifTool

Photo WeatherTag requires that **ExifTool.exe** be copied into the same folder with WeatherTag.exe on your PC. ExifTool is a utility written by Phil Harvey which can read, write and edit file metadata. The latest version of exiftool can be obtained at https://www.sno.phy.queensu.ca/~phil/exiftool/

## Installation

1. On your PC create a folder called "WeatherTag"
2. Copy the WeatherTag.exe file to the folder WeatherTag
3. Copy Exiftool.exe to the Weather Tag folder.
4. Add the Weather Tag folder to the Windows  **path**  variable. If you are not familiar on how to add a folder to the Windows **path**  variable. Search the web for "Add folder to Environment path" for detailed descriptions as it may vary between Windows versions.

## Usage
There are two pieces of information required for Weather Tag to perform its function:
1. Any number of photos taken with correct Date and Time stamp. 
2. A comma separated value file (.csv) in UTF-8 named **weatherhistory.csv** containing periodic weather measurements. The first column values are required to contain a date in MM/DD/YYYY format (Example: 10/5/2018). The second column values are required to contain time values in 12-Hour format (Example: 3:00 PM). The third column should contain ambient temperature values in Celsius. The fourth column values should contain atmospheric humidity in percentage. The fifth column values should contain atmospheric pressure in hectopascals (hPa). The Ambient Temperature, Humidity and Pressure values need not have the measurement units added (°C, %, hPa). 

### Command Line Parameters

WeatherTag supports the following command line parameters:

- **-write** : Writes the weather data to the image files' EXIF metadata. Without this flag, WeatherTag will only display the matched weather data without modifying the files.

- **-threshold=N** : Sets the time threshold in minutes for matching weather readings to photos (default: 30 minutes). Only weather readings within N minutes of the photo's timestamp will be matched.

### Basic Usage

From the Windows Command line, navigate to a folder containing the images you wish to tag as well as the weatherhistory.csv file.

**Display matched weather data without writing to files:**
```
weathertag
```

**Write weather data to image files:**
```
weathertag -write
```

**Write weather data with a custom time threshold (60 minutes):**
```
weathertag -write -threshold=60
```

**Use a stricter time threshold (15 minutes):**
```
weathertag -write -threshold=15
```

### How It Works

When WeatherTag.exe is executed within a folder containing image files and the weatherhistory.csv file, it will:
1. Read the CreateDate from each image file's EXIF metadata
2. Match the closest weather reading within the specified threshold (default 30 minutes)
3. Display the matched weather data for each image
4. If the **-write** flag is used, write the Ambient Temperature, Humidity and Pressure values to the image files' EXIF metadata fields (overwrites original files)

## Example Files
Within the **example** folder there are 5 sample images along with a historical weather log from a weather station near the location where the photos were taken. 

To test Photo WeatherTag with the example files:

1. Open a Command Prompt and navigate to the example folder
2. Run **weathertag** to see the matched weather data without modifying files
3. Run **weathertag -write** to write the weather information to the image files

**Note:** The program uses `-overwrite_original` with exiftool, so original files will be replaced. Make backups if you want to preserve the originals. 

## Download WeatherTag.exe
You can download WeatherTag.exe from the project's GitHub Release page - https://github.com/josemoliver/WeatherTag/releases 

## Build WeatherTag.exe
1. Open the **WeatherTag.sln file** in Visual Studio 2022. 
2. WeatherTag uses Newtonsoft.JSON Nuget Package which should be downloaded using the Nuget Package Manager.
3. Build Solution
4. The **WeatherTag.exe** file should be deposited in the **bin** folder. 
  
## Reading Ambient Temperature, Humidity and Pressure EXIF values
Once the Ambient Temperature, Humidity and Pressure values are written to the jpg image files these can be read with any application or utility which supports reading these tags. Given that the EXIF 2.31 is relatively new, these are few. 

Here are two options:

### Exiftool
Exiftool can read these values using the appropriate command line tags. For example, this command will list these values for all image files contained in the current directory:

`exiftool *.jpg -AmbientTemperature -Humidity -Pressure`

(Reference: Exiftool EXIF tags - https://www.sno.phy.queensu.ca/~phil/exiftool/TagNames/EXIF.html)

### Geosetter
Geosetter (https://www.geosetter.de/en/main-en/) is a Windows freeware application which can read the EXIF 2.31 file metadata.

![Screenshot from Geosetter displaying the weather related EXIF tags for a photo in its Image Info panel.](https://github.com/josemoliver/PhotoWeatherTag/raw/main/blob/geosetter_exif_weather_tags.png "Screenshot from Geosetter displaying the weather related EXIF tags for a photo in its Image Info panel.")
