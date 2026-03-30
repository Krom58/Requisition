using System;
using System.Globalization;

public static class ThaiDateHelper
{
    /// <summary>
    /// แปลง DateTime เป็นรูปแบบวันที่ไทยแบบเต็ม เช่น "25 มกราคม 2568"
    /// </summary>
    public static string ToThaiDate(DateTime date)
    {
        CultureInfo thaiCulture = new CultureInfo("th-TH");
        thaiCulture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
        return date.ToString("dd MMMM yyyy", thaiCulture);
    }

    /// <summary>
    /// แปลง DateTime เป็นรูปแบบวันที่ไทยแบบสั้น เช่น "25/01/2568"
    /// </summary>
    public static string ToThaiDateShort(DateTime date)
    {
        CultureInfo thaiCulture = new CultureInfo("th-TH");
        thaiCulture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
        return date.ToString("dd/MM/yyyy", thaiCulture);
    }

    /// <summary>
    /// แปลง DateTime เป็นรูปแบบวันที่และเวลาไทยแบบสั้น เช่น "25/01/2568 14:30"
    /// </summary>
    public static string ToThaiDateTimeShort(DateTime date)
    {
        CultureInfo thaiCulture = new CultureInfo("th-TH");
        thaiCulture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
        return date.ToString("dd/MM/yyyy HH:mm", thaiCulture);
    }

    /// <summary>
    /// แปลง DateTime เป็นรูปแบบวันที่และเวลาไทยแบบเต็ม เช่น "25/01/2568 14:30:45"
    /// </summary>
    public static string ToThaiDateTimeFull(DateTime date)
    {
        CultureInfo thaiCulture = new CultureInfo("th-TH");
        thaiCulture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
        return date.ToString("dd/MM/yyyy HH:mm:ss", thaiCulture);
    }

    /// <summary>
    /// แปลง DateTime เป็นรูปแบบเดือนปีแบบย่อ เช่น "ม.ค. 2568"
    /// </summary>
    public static string ToThaiMonthYear(DateTime date)
    {
        string[] thaiMonthsShort = {
            "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
            "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."
        };

        int buddhistYear = date.Year + 543;
        return $"{thaiMonthsShort[date.Month - 1]} {buddhistYear}";
    }

    /// <summary>
    /// ดึงชื่อเดือนภาษาไทยแบบเต็มจากหมายเลขเดือน (1-12) เช่น "มกราคม"
    /// </summary>
    public static string GetThaiMonthName(int month)
    {
        string[] thaiMonths = {
            "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
            "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม"
        };

        if (month < 1 || month > 12)
            return "ไม่ระบุ";

        return thaiMonths[month - 1];
    }

    /// <summary>
    /// แปลงปี ค.ศ. (Gregorian) เป็น พ.ศ. (Buddhist)
    /// </summary>
    public static int GregorianToBuddhistYear(int gregorianYear)
    {
        return gregorianYear + 543;
    }

    /// <summary>
    /// แปลงปี พ.ศ. (Buddhist) เป็น ค.ศ. (Gregorian)
    /// </summary>
    public static int BuddhistToGregorianYear(int buddhistYear)
    {
        return buddhistYear - 543;
    }

    /// <summary>
    /// แปลง DateTime? เป็นรูปแบบไทย (รองรับ null)
    /// </summary>
    public static string ToThaiDateOrDefault(DateTime? date, string defaultValue = "-")
    {
        return date.HasValue ? ToThaiDate(date.Value) : defaultValue;
    }

    /// <summary>
    /// แปลง DateTime? เป็นรูปแบบไทยแบบสั้น (รองรับ null)
    /// </summary>
    public static string ToThaiDateShortOrDefault(DateTime? date, string defaultValue = "-")
    {
        return date.HasValue ? ToThaiDateShort(date.Value) : defaultValue;
    }

    /// <summary>
    /// แปลง DateTime? เป็นรูปแบบวันที่และเวลาไทย (รองรับ null)
    /// </summary>
    public static string ToThaiDateTimeShortOrDefault(DateTime? date, string defaultValue = "-")
    {
        return date.HasValue ? ToThaiDateTimeShort(date.Value) : defaultValue;
    }

    /// <summary>
    /// แปลง DateTime? เป็นรูปแบบวันที่และเวลาไทยแบบเต็ม (รองรับ null)
    /// </summary>
    public static string ToThaiDateTimeFullOrDefault(DateTime? date, string defaultValue = "-")
    {
        return date.HasValue ? ToThaiDateTimeFull(date.Value) : defaultValue;
    }
}
