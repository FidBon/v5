using Protocol;

namespace GameLogic;

public static class MilestoneWriter
{
    private const int ExperienceRows = 499;
    private const int ClubRows = 39;

    public static void Write(ByteStreamWriter w, int maximumRank)
    {
        int trophyRows = maximumRank - 1;
        w.WriteVInt(trophyRows + ExperienceRows + ClubRows);

        for (int i = 0; i < trophyRows; i++)
        {
            w.WriteVInt(1);
            w.WriteVInt(i);
            w.WriteVInt(i >= 34
                ? Milestones.ProgressStartTrophies[33] + 50 * (i - 33)
                : Milestones.ProgressStartTrophies[i]);
            w.WriteVInt(i >= 34 ? 50 : Milestones.ProgressTrophies[i]);
            w.WriteVInt(0);
            w.WriteVInt(1);
            w.WriteVInt(1);
            w.WriteVInt(10);
            w.WriteDataReference(5, 1);
            w.WriteVInt(0);
        }

        for (int i = 0; i < ExperienceRows; i++)
        {
            w.WriteVInt(5);
            w.WriteVInt(i);
            w.WriteVInt(Milestones.ProgressStartExp[i]);
            w.WriteVInt(Milestones.ProgressExp[i]);
            w.WriteVInt(0);
            w.WriteVInt(1);
            w.WriteVInt(12);
            w.WriteVInt(20);
            w.WriteDataReference(5, 1);
            w.WriteVInt(0);
        }

        for (int i = 0; i < ClubRows; i++)
        {
            w.WriteVInt(6);
            w.WriteVInt(i);
            w.WriteVInt(Milestones.ProgressStart[i]);
            w.WriteVInt(Milestones.Progress[i]);
            w.WriteVInt(0);

            w.WriteVInt(1);
            w.WriteVInt(13);
            w.WriteVInt(Milestones.PrimaryLevelUpReward[i]);
            w.WriteDataReference(5, 1);

            w.WriteVInt(1);
            w.WriteVInt(13);
            w.WriteVInt(Milestones.SecondaryLevelUpReward[i]);
            w.WriteDataReference(5, 1);
        }
    }
}
