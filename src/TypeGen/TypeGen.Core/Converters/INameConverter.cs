﻿namespace TypeGen.Core.Converters
{
    /// <summary>
    /// Interface for name converters
    /// </summary>
    public interface INameConverter : IConverter
    {
        /// <summary>
        /// Converts a name to a new name
        /// </summary>
        /// <param name="name"></param>
        /// <returns>Converted name</returns>
        string Convert(string name);
    }
}
