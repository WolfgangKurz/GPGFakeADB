#pragma warning disable CA1416 // Validate platform compatibility

using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
[DllImport("user32.dll")]
static extern bool PrintWindow(IntPtr hWnd, IntPtr hDC, uint nFlags);
[DllImport("user32")]
static extern int GetClientRect(IntPtr hWnd, ref RECT lpRect);

[DllImport("user32.dll", CharSet = CharSet.Auto)]
static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, uint wParam, uint lParam);


const int WM_KEYDOWN = 0x0100;
const int WM_KEYUP = 0x0101;

const int WM_MOUSEMOVE = 0x0200;
const int WM_LBUTTONDOWN = 0x0201;
const int WM_LBUTTONUP = 0x0202;

const int PW_CLIENTONLY = 0x01;


const int w = 1280;
const int h = 720;
const string targetGame = "명일방주";

static IntPtr GameHandle() => FindWindow("KIWICROSVM_1", targetGame);

static Rectangle GameSize(IntPtr hWnd) {
	if (hWnd == IntPtr.Zero) return Rectangle.Empty;

	var rc = new RECT();
	GetClientRect(hWnd, ref rc);

	return new Rectangle(0, 0, rc.right - rc.left, rc.bottom - rc.top);
}

static (int, int) InterpolationPoint(int x, int y, int w1, int h1, int w2, int h2) => ((int)((float)x * w2 / w1), (int)((float)y * h2 / h1));

static uint pt2LPARAM(int x, int y, int w1, int h1, int w2, int h2) {
	var (ix, iy) = InterpolationPoint(x, y, w1, h1, w2, h2);
	return (uint)((iy << 16) | ix);
}

static Image? Capture(IntPtr hWnd) {
	if (hWnd == IntPtr.Zero) return null;

	var rc = GameSize(hWnd);

	using var source = new Bitmap(rc.Left + rc.Width, rc.Top + rc.Height);
	using (var g = Graphics.FromImage(source)) {
		var hDC = g.GetHdc();
		PrintWindow(hWnd, hDC, PW_CLIENTONLY);
		g.ReleaseHdc(hDC);
	}

	var bitmap = new Bitmap(w, h);
	using (var g = Graphics.FromImage(bitmap)) {
		g.InterpolationMode = InterpolationMode.Bicubic;
		g.SmoothingMode = SmoothingMode.HighQuality;
		g.DrawImage(
			source,
			new Rectangle(0, 0, w, h),
			new Rectangle(0, 0, rc.Width, rc.Height),
			GraphicsUnit.Pixel
		);
	}

	return bitmap;
}

static void Click(IntPtr hWnd, int x, int y) {
	if (hWnd == IntPtr.Zero) return;

	var size = GameSize(hWnd);
	var lp = pt2LPARAM(x, y, w, h, size.Width, size.Height);
	SendMessage(hWnd, WM_LBUTTONDOWN, 1, lp);
	SendMessage(hWnd, WM_LBUTTONUP, 1, lp);
}
static void Swipe(IntPtr hWnd, int x1, int y1, int x2, int y2, int duration) {
	if (hWnd == IntPtr.Zero) return;

	const int interval = 4;
	var durTick = duration * TimeSpan.TicksPerSecond;

	var begin = DateTime.Now.Ticks;
	var end = DateTime.Now.Ticks + durTick;

	var size = GameSize(hWnd);
	SendMessage(hWnd, WM_MOUSEMOVE, 1, pt2LPARAM(x1, y1, w, h, size.Width, size.Height));

	while (DateTime.Now.Ticks < end) {
		var elapsed = DateTime.Now.Ticks - begin;
		var ratio = (float)elapsed / durTick;

		var x = x1 + (x2 - x1) * ratio;
		var y = y1 + (y2 - y1) * ratio;
		var pos = pt2LPARAM((int)x, (int)y, w, h, size.Width, size.Height);
		SendMessage(hWnd, WM_MOUSEMOVE, 1, pos);

		Thread.Sleep(interval);
	}

	SendMessage(hWnd, WM_LBUTTONUP, 1, pt2LPARAM(x2, y2, w, h, size.Width, size.Height));
}


static void KeyEvent(IntPtr hWnd, uint keyCode) {
	if (hWnd == IntPtr.Zero) return;

	SendMessage(hWnd, WM_KEYDOWN, keyCode, (keyCode << 16));
	SendMessage(hWnd, WM_KEYUP, keyCode, (keyCode << 16) | 0xC0000000);
}

static ImageCodecInfo GetEncoder(ImageFormat format) {
	var codecs = ImageCodecInfo.GetImageDecoders();
	foreach (var codec in codecs) {
		if (codec.FormatID == format.Guid)
			return codec;
	}
	return codecs[1];
}

var arg = string.Join(" ", args);

if (arg.Contains("connect")) {
	Console.WriteLine("connected to Google Play Games");
}
else if (arg.Contains("shell input tap")) {
	var x = int.Parse(args[5]);
	var y = int.Parse(args[6]);
	Click(GameHandle(), x, y);
}
else if (arg.Contains("shell input swipe")) {
	var x1 = int.Parse(args[5]);
	var y1 = int.Parse(args[6]);
	var x2 = int.Parse(args[7]);
	var y2 = int.Parse(args[8]);
	var dur = int.Parse(args[9]);
	Swipe(GameHandle(), x1, y1, x2, y2, dur);
}
else if (arg.Contains("shell input keyevent 111")) {
	KeyEvent(GameHandle(), 0x01);
}
else if (arg.Contains("shell dumpsys window displays")) {
	Console.WriteLine($"{w}"); // 1280 720 fixed
	Console.WriteLine($"{h}");
}
else if (arg.Contains("exec-out screencap -p")) {
	var ptr = GameHandle();
	var bitmap = Capture(ptr);

	if (bitmap == null) return;

	using (MemoryStream memStream = new MemoryStream()) {
		var encoder = GetEncoder(ImageFormat.Png);
		var eParams = new EncoderParameters(1);
		eParams.Param[0] = new EncoderParameter(Encoder.Quality, 100L);

		bitmap.Save(memStream, encoder, eParams);

		var outStream = Console.OpenStandardOutput();
		outStream.Write(memStream.GetBuffer(), 0, (int)memStream.Length);
	}
}

struct RECT {
	public int left;
	public int top;
	public int right;
	public int bottom;
}
