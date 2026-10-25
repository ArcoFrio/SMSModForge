using System;
using System.Collections.Generic;

namespace SMSModForge.ViewModel;

/// <summary>A record whose runtime name can follow its display name - see
/// <see cref="DerivedKey"/> for the rule.</summary>
internal interface IDerivesKey
{
    /// <summary>Just made - added, duplicated or pasted: the runtime name
    /// follows the display name.</summary>
    void DeriveKeyFromDisplayName(Func<IEnumerable<string>> siblingKeys);

    /// <summary>Loaded from disk: follows from the next rename if the runtime
    /// name is still the display name made into a name.</summary>
    void WatchDisplayName(Func<IEnumerable<string>> siblingKeys);
}
