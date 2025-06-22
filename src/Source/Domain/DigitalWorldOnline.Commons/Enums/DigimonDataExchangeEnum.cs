namespace DigitalWorldOnline.Commons.Enums
{
    public enum DigimonDataExchangeEnum
    {
        eDataChangeType_None = -1,
        eDataChangeType_Begin = 0,
        eDataChangeType_Size = eDataChangeType_Begin,
        eDataChangeType_Inchant,
        eDataChangeType_EvoSlot,
        eDataChangeType_End = eDataChangeType_EvoSlot,
        eDataChangeType_Count,
    }

    public enum DigimonDataExchangeResultEnum
    {
        MESSAGE_LACK = 11040,	            // You are missing the item (required to exchange the item).
        MESSAGE_MISMATCH_HATCHLV = 30799,	// Data exchange between Transcendent Digimon requires Transcendent Digimon of the same series.
        MESSAGE_REGISTER = 30800,	        // Register everyone
        MESSAGE_MISMATCH = 30801,	        // Only Digimon of the same series can be registered.
        MESSAGE_PARTNERMON = 30802,	        // Partner digimon cannot be registered.
        MESSAGE_COMPLETE = 30803,	        // You are done.
        MESSAGE_ACTION = 30804,	            // Do you want to run it?
        MESSAGE_SAME = 30805,	            // They are identical and cannot exchange data.

        NONE_SLOT = -1,
        NONE_MODEL = -1
    }
}
