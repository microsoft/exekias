using System;
using System.Threading.Tasks;

namespace Exekias.Core.Tests
{

    public class ImporterMock : IImporter
    {
        public bool CanImport(string path)
        {
            return path.EndsWith(".data");
        }

    }
}
