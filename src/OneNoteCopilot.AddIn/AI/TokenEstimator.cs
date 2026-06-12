namespace OneNoteCopilot.AI
{
    public static class TokenEstimator
    {
        public static int Estimate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int chineseChars = 0;
            int otherChars = 0;

            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    chineseChars++;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    otherChars++;
                }
            }

            return (int)(chineseChars * 1.5 + otherChars / 4.0) + 1;
        }
    }
}
