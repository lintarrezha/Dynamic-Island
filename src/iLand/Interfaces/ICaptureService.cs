namespace iLand.Interfaces;

public interface ICaptureService
{
    /// <summary>Dipanggil tepat sebelum pengambilan layar dimulai (island akan menutup).</summary>
    event EventHandler? CaptureStarted;

    /// <summary>Ambil seluruh layar pada monitor tempat kursor berada.</summary>
    void CaptureFullScreen();

    /// <summary>Layar dibekukan, lalu pengguna menyeret kotak untuk memilih area.</summary>
    void CaptureArea();

    /// <summary>Layar dibekukan, lalu pengguna mengklik sebuah jendela.</summary>
    void CaptureWindow();
}
