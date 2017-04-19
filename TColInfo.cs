using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace xtail {
  /// <summary>
  /// A column structure as read from command line
  /// </summary>
  public class TColInfo {
    public TColInfo()
        : this("", 0, false) {
      }
      public TColInfo( string name, int length, bool enabled ) {
        Name = name;
        Length = length;
        Enabled = enabled;
      }
      public string Name;
      public int Length;
      public bool Enabled;
  }
}
