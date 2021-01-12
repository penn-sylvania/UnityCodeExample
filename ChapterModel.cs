using EasyMobile;
using I2.Loc;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Assertions;

namespace ComicBook
{
    public class ChapterModel : Model
    {
        [SerializeField] private string iapProductId;

        // true if chapter pages are available in the application and don't have to be downloaded
        [SerializeField] private bool isPreloaded;

        [SerializeField] private ChaptersInfoDictionary chaptersInfo;

        [SerializeField] private string notificationTitle = "";
        [SerializeField] private string notificationBody = "";

        [SerializeField] private string smallIconName = "ic_stat_push_icon";
        
        // temporary unavailable
        //[SerializeField] private string largeIconName = "ic_stat_push_icon";

        private bool isInLoading;
        private bool isInReloading;
        private bool isLocked = true;
        private bool isLoaded = false;

        private Hash128 version;
        private AssetBundle assetBundle;
        private WWW currentWebRequest;
        private string prevAssetPath = "";

        private ChapterSettings settings;
        private int bookId;

        private string priceString = "";
        private string notificationId = "";

        public string IAPProductId => iapProductId;

        public override bool IsAvailable => !IAPProductId.Equals("");

        public int BookId
        {
            get => bookId;
            set
            {
                bookId = value;
            }
        }

        public bool HasChapter => true;
        public bool IsLocked => isLocked;
        public bool IsLoaded => isLoaded && ChapterSettings != null;
        public bool IsPreloaded => isPreloaded;
        public bool IsInRead => Game.Settings.ChapterNum == Number && Game.Settings.BookNum == BookId;
        public bool IsInLoading => isInLoading;
        public bool IsInReloading => isInReloading;
        public ChapterSettings ChapterSettings => settings;
        public int LoadingProgress => (int)((currentWebRequest?.progress  ?? (IsLoaded ? 1 : 0)) * 100f);
        public string PriceString => priceString;

        public override string Name => chaptersInfo[Game.Settings.Language].ChapterName;
        private string BundleURL => chaptersInfo[Game.Settings.Language].BundleURL;
        private string ManifestURL => chaptersInfo[Game.Settings.Language].ManifestURL; 
        private string AssetName => chaptersInfo[Game.Settings.Language].AssetName;

        public event Action SettingsChanged;
        public event Action ChapterLoadingStarted;
        public event Action ChapterLoadingFinished;

        // public methods

        public void ReloadAssetBundle()
        {
            if (IsPreloaded)
            {
                string path = GetBundlePathForLoadFromFile(BundleURL);
                StartCoroutine(LoadBundleFromStreamingAssetsRoutine(path));
            }
            else
            {
                if (assetBundle != null)
                {
                    ClearCache();
                }
                LoadAssetBundles(true);
            }
        }

        public void Unlock()
        {
            isLocked = false;

            SettingsChanged?.Invoke();
            Save();
        }

        public void LoadAssetBundles(bool isSilent = false)
        {
            if (!string.IsNullOrEmpty(BundleURL) && (!isSilent || isLoaded) && ChapterSettings == null && !IsLocked)
            {
                StartCoroutine(DownloadAndCache(isSilent));
            }
        }

        public void ClearCache()
        {
            Assert.IsNotNull(assetBundle);

            string name = assetBundle.name;

            Uri uri = new Uri(BundleURL);
            string bundleName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);

            assetBundle.Unload(true);
            bool isCleared = Caching.ClearAllCachedVersions(bundleName);

            if (isCleared)
            {
                isLoaded = false;
                SettingsChanged?.Invoke();
            }

            bool isCached = Caching.IsVersionCached(BundleURL, version);
            Save();
        }

        public IEnumerator LoadManifest(string path)
        {
            using (WWW www = new WWW(path))
            {
                yield return www;

                if (!string.IsNullOrEmpty(www.error))
                {
                    Debug.Log(www.error);

                    yield break;
                }

                version = Hash128.Parse(www.text);
            }
        }

        // protected methods

        protected override void Awake()
        {
            base.Awake();

            if (!RuntimeManager.IsInitialized())
            {
                RuntimeManager.Init();
            }

            ReloadAssetBundle();

            StartCoroutine(UpdatePriceLabel());

            Game.Settings.LanguageChangedEvent += OnLanguageChanged;
        }

        protected void Start()
        {
            StartCoroutine(CheckOwnedProduct());

            if (!notificationId.Equals(""))
            {
                Notifications.CancelPendingLocalNotification(notificationId);
            }

            if (IsLockedByTime && notificationTitle != "")
            {
                StartCoroutine(ScheduleLocalNotification());
            }
        }

        protected override void Load()
        {
            bool isLockedValue = PlayerPrefs.GetInt($"{IAPProductId}_IsLocked", 1) == 1;
            isLoaded = PlayerPrefs.GetInt($"{IAPProductId}_IsLoaded", 0) == 1;
            version = Hash128.Parse(PlayerPrefs.GetString($"{IAPProductId}_Version", "0"));
            isLocked = isLockedValue;

            priceString = PlayerPrefs.GetString($"{IAPProductId}_PriceString", "");
            notificationId = PlayerPrefs.GetString($"{IAPProductId}_NotificationId", "");
        }

        protected override void Save()
        {
            PlayerPrefs.SetInt($"{IAPProductId}_IsLocked", IsLocked ? 1 : 0);
            PlayerPrefs.SetInt($"{IAPProductId}_IsLoaded", isLoaded ? 1 : 0);
            string s = version.ToString();
            PlayerPrefs.SetString($"{IAPProductId}_Version", s);
            PlayerPrefs.SetString($"{IAPProductId}_PriceString", priceString);
            PlayerPrefs.SetString($"{IAPProductId}_NotificationId", notificationId);

            PlayerPrefs.Save();
        }

        // private methods

        private IEnumerator DownloadAndCache(bool isSilent)
        {
            while (!Caching.ready)
            {
                yield return null;
            }

            Debug.Log($"Dyakova.DownloadAndCache Caching.ready");

            isInReloading = true;

            ChapterLoadingStarted?.Invoke();

            yield return LoadManifest(ManifestURL);

            Debug.Log($"Dyakova.DownloadAndCache LoadManifest true");

            if (!isSilent)
            {
                isInLoading = true;
            }

            currentWebRequest = WWW.LoadFromCacheOrDownload(BundleURL, version);
            Debug.Log($"Dyakova.DownloadAndCache currentWebRequest = {currentWebRequest}");

            yield return currentWebRequest;

            Debug.Log($"Dyakova.DownloadAndCache currentWebRequest true");

            if (!string.IsNullOrEmpty(currentWebRequest.error))
            {
                isInLoading = false;
                Debug.Log(currentWebRequest.error);

                yield break;
            }

            Debug.Log("Dyakova: Bundle loaded successfully");

            assetBundle = currentWebRequest.assetBundle;

            AssetBundleRequest settingsRequest = assetBundle.LoadAssetAsync(AssetName, typeof(ChapterSettings));
            yield return settingsRequest;

            settings = settingsRequest.asset as ChapterSettings;

            currentWebRequest.Dispose();
            currentWebRequest = null;

            isLoaded = true;

            if (!isSilent)
            {
                isInLoading = false;
            }
            Save();

            SettingsChanged?.Invoke();
            ChapterLoadingFinished?.Invoke();
            isInReloading = false;

            UpdatePriceLabel();
        }

        private string GetBundlePathForLoadFromFile(string relativePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var streamingAssetsPath = Application.dataPath + "!assets/";
#else
            var streamingAssetsPath = Application.streamingAssetsPath;
#endif
            string res = Path.Combine(streamingAssetsPath, relativePath);
            return res;
        }

        private IEnumerator LoadBundleFromStreamingAssetsRoutine(string relativePath)
        {
            isInReloading = true;
            ChapterLoadingStarted?.Invoke();

            assetBundle?.Unload(true);

            assetBundle = AssetBundle.LoadFromFile(GetBundlePathForLoadFromFile(relativePath));

            var settingsRequest = assetBundle.LoadAssetAsync(AssetName, typeof(ChapterSettings));
            yield return settingsRequest;

            settings = settingsRequest.asset as ChapterSettings;

            isLoaded = true;
            SettingsChanged?.Invoke();
            ChapterLoadingFinished?.Invoke();
            isInReloading = false;
        }

        private IEnumerator CheckOwnedProduct()
        {
            // Wait until the module is initialized
            if (!InAppPurchasing.IsInitialized())
            {
                yield return new WaitUntil(() => InAppPurchasing.IsInitialized());
            }

            IAPProduct iapProduct = InAppPurchasing.GetIAPProductById(iapProductId);
            if (iapProduct != null && InAppPurchasing.IsProductOwned(iapProduct.Name))
            {
                Unlock();
            }
        }

        private void OnLanguageChanged(LanguageKind kind)
        {
            StartCoroutine(UpdatePriceLabel());
        }

        private IEnumerator UpdatePriceLabel()
        {
            // Wait until the module is initialized
            if (!InAppPurchasing.IsInitialized())
            {
                yield return new WaitUntil(() => InAppPurchasing.IsInitialized());
            }

            string priceStringTmp = GetPriceString();
            if (!priceStringTmp.Equals(""))
            {
                priceString = priceStringTmp;
            }

            SettingsChanged?.Invoke();
            Save();
        }

        private string GetPriceString()
        {
            IAPProduct iapProduct = InAppPurchasing.GetIAPProductById(iapProductId);
            if (iapProduct != null)
            {
                var product = InAppPurchasing.GetProduct(iapProduct.Name);
                var localizedData = product.metadata;
                if (localizedData != null)
                {
                    string iso = localizedData.isoCurrencyCode;

                    string str = "";
                    if (iso.Equals("RUB"))
                    {
                        str = $"₽ {localizedData.localizedPrice}";
                    }
                    else if (iso.Equals("UAH"))
                    {
                        str = $"₴ {localizedData.localizedPrice}";
                    }
                    else
                    {
                        str = localizedData.localizedPriceString;
                    }
                    return str;
                }
                Debug.LogError($"Dyakova: There is no localizedData for {iapProductId}");
                return "";
            }
            Debug.LogError($"Dyakova: There is no iapProduct for {iapProductId}");
            return "";
        }

        // notifications

        private NotificationContent PrepareNotificationContent()
        {
            NotificationContent content = new NotificationContent();

            // Provide the notification title.
            content.title = LocalizationManager.GetTranslation(notificationTitle);

            // Provide the notification message.
            content.body = LocalizationManager.GetTranslation(notificationBody);

            // If you want to use default small icon and large icon (on Android),
            // don't set the smallIcon and largeIcon fields of the content.
            // If you want to use custom icons instead, simply specify their names here (without file extensions).
            content.smallIcon = smallIconName;
            //content.largeIcon = largeIconName;

            return content;
        }

        private IEnumerator ScheduleLocalNotification()
        {
            // Wait until the module is initialized
            if (!Notifications.IsInitialized())
            {
                yield return new WaitUntil(() => Notifications.IsInitialized());
            }

            // Prepare the notification content (see the above section).
            NotificationContent content = PrepareNotificationContent();

            // Set the delivery time.
            DateTime triggerDate = new DateTime(yearOfRelease, monthOfRelease, dayOfRelease, hourOfRelease, 0, 0);

            // Schedule the notification.
            notificationId = Notifications.ScheduleLocalNotification(triggerDate, content);
            Debug.Log($"Dyakova: ScheduleLocalNotification added {notificationId}");

            // method to debug pending notifications
            //GetPendingLocalNotifications();
        }

        // debug methods for notifications

        private void GetPendingLocalNotifications()
        {
            Notifications.GetPendingLocalNotifications(GetPendingLocalNotificationsCallback);
        }

        private void GetPendingLocalNotificationsCallback(NotificationRequest[] pendingRequests)
        {
            foreach (var request in pendingRequests)
            {
                NotificationContent content = request.content;

                Debug.Log("Dyakova.Notification request ID: " + request.id);
                Debug.Log("Dyakova.Notification title: " + content.title);
                Debug.Log("Dyakova.Notification body: " + content.body);
            }
        }

        [Serializable]
        // SerializableDictionary is a custom class
        public class ChaptersInfoDictionary : SerializableDictionary<LanguageKind, ChapterInfo> { }

        [Serializable]
        public class ChapterInfo
        {
            [SerializeField] private string chapterName;
            [SerializeField] private string bundleURL;
            [SerializeField] private string manifestURL;
            [SerializeField] private string assetName;

            public string ChapterName => chapterName;
            public string BundleURL => bundleURL;
            public string ManifestURL => manifestURL;
            public string AssetName => assetName;
        }
    }
}
