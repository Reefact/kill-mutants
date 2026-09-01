namespace Sample.Generator.Support
{
    /// <summary>
    /// The generator's own dependency. It exists only to be a second assembly the generator has to
    /// load at run time, which is the thing under test.
    /// </summary>
    public static class Naming
    {
        public static string PropertyFor(string field)
        {
            return char.ToUpperInvariant(field[0]) + field.Substring(1);
        }
    }
}
