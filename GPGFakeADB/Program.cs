#pragma warning disable CA1416 // Validate platform compatibility

using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

using GameHandle = System.Tuple<System.IntPtr, System.IntPtr>;

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpClassName, string? lpWindowName);
[DllImport("user32.dll")]
static extern bool PrintWindow(IntPtr hWnd, IntPtr hDC, uint nFlags);
[DllImport("user32")]
static extern int GetWindowRect(IntPtr hWnd, ref RECT lpRect);

[DllImport("user32.dll", CharSet = CharSet.Auto)]
static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, uint wParam, uint lParam);


const int WM_KEYDOWN = 0x0100;
const int WM_KEYUP = 0x0101;

const int WM_MOUSEMOVE = 0x0200;
const int WM_LBUTTONDOWN = 0x0201;
const int WM_LBUTTONUP = 0x0202;


const int w = 1280;
const int h = 720;
const string targetGame = "명일방주";

static GameHandle GamePtr() {
	var ptr = FindWindow("KIWICROSVM_1", targetGame);
	if (ptr != IntPtr.Zero)
		return new GameHandle(ptr, FindWindowEx(ptr, IntPtr.Zero, "subWin", "sub"));

	return new GameHandle(IntPtr.Zero, IntPtr.Zero);
}

static Rectangle GameSize(GameHandle handle) {
	var (ptr, sub) = handle;
	if (ptr == IntPtr.Zero || sub == IntPtr.Zero) return Rectangle.Empty;

	var outer = new RECT();
	var inner = new RECT();
	GetWindowRect(ptr, ref outer);
	GetWindowRect(sub, ref inner);

	var left = inner.left - outer.left;
	var top = inner.top - outer.top;
	var width = inner.right - inner.left;
	var height = inner.bottom - inner.top;
	return new Rectangle(left, top, width, height);
}

static (int, int) InterpolationPoint(int x, int y, int w1, int h1, int w2, int h2) {
	var rx = (float)x / w1;
	var ry = (float)y / h1;

	return ((int)(rx * w2), (int)(ry * h2));
}

static uint pt2LPARAM(int x, int y, int w1, int h1, int w2, int h2) {
	var (ix, iy) = InterpolationPoint(x, y, w1, h1, w2, h2);
	return (uint)((iy << 16) | ix);
}

static Image? Capture(GameHandle handle) {
	var (ptr, sub) = handle;
	if (ptr == IntPtr.Zero || sub == IntPtr.Zero) return null;

	var rc = GameSize(handle);

	var bitmap = new Bitmap(w, h);
	using var outerBitmap = new Bitmap(rc.Left + rc.Width, rc.Top + rc.Height);
	using (var g = Graphics.FromImage(outerBitmap)) {
		var hDC = g.GetHdc();
		PrintWindow(ptr, hDC, 0);
		g.ReleaseHdc(hDC);
	}

	using (var g = Graphics.FromImage(bitmap)) {
		g.InterpolationMode = InterpolationMode.Bicubic;
		g.SmoothingMode = SmoothingMode.HighQuality;
		g.DrawImage(
			outerBitmap,
			new Rectangle(0, 0, w, h),
			new Rectangle(rc.Left, rc.Top, rc.Width, rc.Height),
			GraphicsUnit.Pixel
		);
	}

	return bitmap;
}

static void Click(GameHandle handle, int x, int y) {
	var (ptr, sub) = handle;
	if (ptr == IntPtr.Zero) return;

	var size = GameSize(handle);
	var lp = pt2LPARAM(x, y, w, h, size.Width, size.Height);
	SendMessage(ptr, WM_LBUTTONDOWN, 1, lp);
	SendMessage(ptr, WM_LBUTTONUP, 1, lp);
}
static void Swipe(GameHandle handle, int x1, int y1, int x2, int y2, int duration) {
	var (ptr, sub) = handle;
	if (ptr == IntPtr.Zero) return;

	const int interval = 4;
	var durTick = duration * TimeSpan.TicksPerSecond;

	var begin = DateTime.Now.Ticks;
	var end = DateTime.Now.Ticks + durTick;

	var size = GameSize(handle);
	SendMessage(ptr, WM_MOUSEMOVE, 1, pt2LPARAM(x1, y1, w, h, size.Width, size.Height));

	while (DateTime.Now.Ticks < end) {
		var elapsed = DateTime.Now.Ticks - begin;
		var ratio = (float)elapsed / durTick;

		var x = x1 + (x2 - x1) * ratio;
		var y = y1 + (y2 - y1) * ratio;
		var pos = pt2LPARAM((int)x, (int)y, w, h, size.Width, size.Height);
		SendMessage(ptr, WM_MOUSEMOVE, 1, pos);

		Thread.Sleep(interval);
	}

	SendMessage(ptr, WM_LBUTTONUP, 1, pt2LPARAM(x2, y2, w, h, size.Width, size.Height));
}


static void KeyEvent(IntPtr ptr, uint keyCode) {
	if (ptr == IntPtr.Zero) return;

	SendMessage(ptr, WM_KEYDOWN, keyCode, (keyCode << 16));
	SendMessage(ptr, WM_KEYUP, keyCode, (keyCode << 16) | 0xC0000000);
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
	Click(GamePtr(), x, y);
}
else if (arg.Contains("shell input swipe")) {
	var x1 = int.Parse(args[5]);
	var y1 = int.Parse(args[6]);
	var x2 = int.Parse(args[7]);
	var y2 = int.Parse(args[8]);
	var dur = int.Parse(args[9]);
	Swipe(GamePtr(), x1, y1, x2, y2, dur);
}
else if (arg.Contains("shell input keyevent 111")) {
	var (ptr, _) = GamePtr();
	KeyEvent(ptr, 0x01);
}
else if (arg.Contains("shell dumpsys window displays")) {
	Console.WriteLine($"{w}"); // 1280 720 fixed
	Console.WriteLine($"{h}");
}
else if (arg.Contains("exec-out screencap -p")) {
	var handle = GamePtr();
	var bitmap = Capture(handle);

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
