using I2.Loc;
using System;
using UnityEngine;

namespace ComicBook
{
    public abstract class Model : MonoBehaviour
    {
        [SerializeField] private Sprite image;
        [SerializeField] private int modelNumber;

        [SerializeField] private bool hasReleaseTime = false;
        [SerializeField] protected int yearOfRelease = 0;
        [SerializeField] protected int monthOfRelease = 0;
        [SerializeField] protected int dayOfRelease = 0;
        [SerializeField] protected int hourOfRelease = 0;
        [SerializeField] protected int minuteOfRelease = 0;

        public Sprite Image => image;

        public int Number => modelNumber;

        public bool IsLockedByTime => hasReleaseTime && releaseTime > DateTime.Now;

        // inaccuracy of time display is agreed with the customer
        public string ReleaseTimeString
        {
            get
            {
                string res = "";
                var deltaTime = releaseTime - DateTime.Now;
                int monthsCount = deltaTime.Days / 30;
                res = monthsCount == 0 ? "" : $"{monthsCount}{LocalizationManager.GetTranslation("Months")}:";

                int daysCount = deltaTime.Days % 30;
                res += (daysCount == 0 && res == "") ? "" : $"{daysCount}{LocalizationManager.GetTranslation("Days")}:";

                int hoursCount = deltaTime.Hours;
                res += (hoursCount == 0 && res == "") ? "" : $"{hoursCount}{LocalizationManager.GetTranslation("Hours")}";
                return res;
            }
        }

        public abstract bool IsAvailable { get; }

        public abstract string Name { get; }

        private bool isLoadedFromSaves = false;

        private DateTime releaseTime;
        private bool prevLockedByTimeStatus;

        public event Action UnlockedByTime;

        protected virtual void Awake()
        {
            if (!isLoadedFromSaves)
            {
                Load();
                isLoadedFromSaves = true;
            }

            if (hasReleaseTime)
            {
                releaseTime = new DateTime(yearOfRelease, monthOfRelease, dayOfRelease, hourOfRelease, minuteOfRelease, 0);
            }
            prevLockedByTimeStatus = IsLockedByTime;
        }

        protected virtual void Update()
        {
            if (prevLockedByTimeStatus && !IsLockedByTime)
            {
                prevLockedByTimeStatus = false;
                UnlockedByTime?.Invoke();
            }
        }

        protected abstract void Load();

        protected abstract void Save();
    }
}
