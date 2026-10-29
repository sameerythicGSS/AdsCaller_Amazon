/// <summary>
/// Holds all enums for the Unity project.
/// </summary>
public class EnumsJar_Salon
{
    public enum ToDo
    {
        None,
        Show,
        Hide
    }

    public enum ProductType
    {
        Consumable,
        NonConsumable,
        Subscription
    }

    public enum AdsMode
    {
        Online,
        CrossPromo,
        None
    }

    public enum VideoCacheMode
    {
        /// <summary>
        /// Downloads all remote videos to the device immediately when the catalog loads.
        /// </summary>
        PreloadOnLaunch,

        /// <summary>
        /// Downloads the remote video right before the ad is displayed.
        /// </summary>
        LazyLoadOnShow,

            /// <summary>
            /// Streams the video directly from the remote URL without saving it to disk.
            /// </summary>
        Stream
    }

    public enum SubscriptionDuration
    {
        Weekly,
        Monthly,
        Yearly
    }

    public enum BannerTransitionType
    {
        None,
        SlotSpin,
        TileFlip
    }

    public enum AdPlacementType
    {
        Banner,
        Inter,
        LBanner,
        PopAd,
        Rewarded
    }

    public enum AdSequenceType
    {
        Sequential,
        Random
    }

    public enum PlatformType
    {
        Android,
        iOS,
        Amazon
    }

    public enum AdMediaType
    {
        Image,
        Video
    }

    public enum AdPosition
    {
        TopLeft = 1,
        TopCenter = 2,
        TopRight = 3,

        RightCenter = 4,
        MiddleCenter = 5,
        LeftCenter = 6,

        BottomCenter = 7,
        BottomRight = 8,
        BottomLeft = 9,

        Neither = 10
    }

    public enum AppOpenStrategy
    {
        StartAndResume = 0,
        Standard = 1,
        Delayed = 2
    }
}
