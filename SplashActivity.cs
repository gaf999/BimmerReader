using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace BimmerReader
{
    [Activity(
        Label = "@string/app_name",
        MainLauncher = true,
        Theme = "@style/Theme.BimmerReader.Splash",
        ScreenOrientation = ScreenOrientation.Portrait)]
    public class SplashActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Android 12+ (API 31) shows its own splash screen automatically,
            // styled by our Theme.BimmerReader.Splash (values-v31/themes.xml).
            // Go straight to MainActivity so the user never sees two splash screens.
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                GoToMain();
                return;
            }

            // Pre-Android 12: show our custom splash for 2 seconds.
            SetContentView(Resource.Layout.activity_splash);
            new Handler(Looper.MainLooper!).PostDelayed(GoToMain, 2000);
        }

        private void GoToMain()
        {
            StartActivity(new Intent(this, typeof(MainActivity)));
            Finish();
        }
    }
}
