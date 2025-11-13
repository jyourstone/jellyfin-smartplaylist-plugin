# Nullable Reference Types Fix Plan

## Overview
This document outlines an incremental plan to fix nullable reference type warnings/errors in the SmartLists plugin. The goal is to achieve full compliance with Jellyfin plugin template guidelines while maintaining code quality and avoiding breaking changes.

## Current Status
- **Total Errors**: ~200+ nullable reference type errors (after enabling nullable)
- **Error Types** (by frequency):
  - `CS8604`: Possible null reference argument (~80 errors) - Logger parameters, method arguments
  - `CS8625`: Cannot convert null literal to non-nullable reference type (~40 errors) - Default null values
  - `CS8618`: Non-nullable property must contain non-null value (~30 errors) - Uninitialized properties
  - `CS8603`: Possible null reference return (~20 errors) - Methods that can return null
  - `CS8622`: Nullability mismatch in delegate signatures (~15 errors) - Event handlers, timer callbacks
  - `CS8767`: Nullability mismatch in interface implementations (~5 errors) - IComparer, IComparable, ILogger
  - `CS8600/CS8601/CS8602`: Various null reference conversions (~10 errors)
  - `CS8633`: Nullability in generic constraints mismatch (~2 errors)

## Strategy

### Phase 1: Foundation (Low Risk)
**Goal**: Fix obvious null assignments and add null checks where appropriate

#### 1.1 Simple Null Assignments (CS8625)
**Files**: `Engine.cs`, `Factory.cs`, `PlaylistService.cs`, `SmartList.cs`

**Approach**:
- Replace `null` with `null!` (null-forgiving operator) where null is intentionally allowed
- Add null checks before usage
- Use null-conditional operators (`?.`) where appropriate

**Estimated Effort**: 2-3 hours
**Risk**: Low

#### 1.2 Logger Parameters
**Files**: `Engine.cs`, `SmartList.cs`, `PlaylistService.cs`

**Approach**:
- Change `ILogger logger = null` to `ILogger? logger = null`
- Add null checks before logger calls: `logger?.LogDebug(...)`

**Estimated Effort**: 1 hour
**Risk**: Low

### Phase 2: Interface Implementations (Medium Risk)
**Goal**: Fix nullable annotations in interface implementations

#### 2.1 IComparer Implementation
**File**: `SmartList.cs` (NaturalStringComparer)

**Issue**: `Compare(string x, string y)` doesn't match `IComparer<string>.Compare(string? x, string? y)`

**Fix**:
```csharp
public int Compare(string? x, string? y)
{
    if (x == null && y == null) return 0;
    if (x == null) return -1;
    if (y == null) return 1;
    // ... rest of implementation
}
```

**Estimated Effort**: 30 minutes
**Risk**: Low-Medium

#### 2.2 IComparable Implementation
**File**: `SmartList.cs` (ComparableTuple4)

**Issue**: `CompareTo(object obj)` doesn't match `IComparable.CompareTo(object? obj)`

**Fix**:
```csharp
public int CompareTo(object? obj)
{
    if (obj == null) return 1;
    // ... rest of implementation
}
```

**Estimated Effort**: 30 minutes
**Risk**: Low-Medium

#### 2.3 ILogger Implementation
**File**: `SmartListController.cs` (PlaylistServiceLogger)

**Issues**:
- `Log<TState>(..., Exception exception, ...)` should be `Exception? exception`
- `BeginScope<TState>(TState state)` constraint mismatch

**Fix**:
- Change `Exception exception` to `Exception? exception`
- Use explicit interface implementation for `BeginScope` if needed

**Estimated Effort**: 1 hour
**Risk**: Medium

### Phase 3: Method Parameters and Return Types (Medium Risk)
**Goal**: Add proper nullable annotations to method signatures

#### 3.1 Optional Parameters
**Files**: Multiple files

**Approach**:
- Review methods with optional parameters that accept null
- Add `?` to parameter types where null is valid
- Update call sites if needed

**Estimated Effort**: 2-3 hours
**Risk**: Medium

#### 3.2 Dictionary/Collection Access
**Files**: `SmartList.cs`, `PlaylistService.cs`

**Approach**:
- Use `TryGetValue` instead of direct indexing where null is possible
- Add null checks after dictionary access
- Use null-conditional operators

**Estimated Effort**: 2 hours
**Risk**: Medium

### Phase 4: Complex Scenarios (Higher Risk)
**Goal**: Fix complex nullable scenarios that may require refactoring

#### 4.1 Generic Type Constraints
**File**: `SmartListController.cs`

**Issue**: Generic constraint nullability mismatch

**Approach**:
- Review generic type constraints
- Use explicit interface implementation if needed
- Consider using `where TState : notnull` if appropriate

**Estimated Effort**: 1-2 hours
**Risk**: Medium-High

#### 4.2 Reflection and Dynamic Code
**Files**: `Factory.cs`, `SmartList.cs`

**Approach**:
- Add null checks after reflection calls
- Use null-forgiving operator (`!`) where we know values won't be null
- Add defensive null checks

**Estimated Effort**: 2-3 hours
**Risk**: Medium

## Implementation Order

### Week 1: Foundation
1. ✅ Update csproj with nullable settings (DONE)
2. Fix Phase 1.1: Simple null assignments
3. Fix Phase 1.2: Logger parameters

### Week 2: Interfaces
4. Fix Phase 2.1: IComparer implementation
5. Fix Phase 2.2: IComparable implementation
6. Fix Phase 2.3: ILogger implementation

### Week 3: Methods and Collections
7. Fix Phase 3.1: Optional parameters
8. Fix Phase 3.2: Dictionary/Collection access

### Week 4: Complex Scenarios
9. Fix Phase 4.1: Generic constraints
10. Fix Phase 4.2: Reflection code

## Testing Strategy

After each phase:
1. Build the project to verify no new errors introduced
2. Run existing tests (if any)
3. Manual testing of affected functionality
4. Commit changes with descriptive messages

## Quick Wins (Can be done immediately)

### Priority 1: Logger Parameters (Highest Impact)
**Change**: `ILogger logger = null` → `ILogger? logger = null`
- **Files**: `Engine.cs`, `SmartList.cs`, `PlaylistService.cs`, `Factory.cs`
- **Impact**: ~80 errors fixed (CS8604)
- **Risk**: Very low
- **Time**: 30 minutes

### Priority 2: Property Initialization (High Impact)
**Change**: Add `= null!` or `= new()` to uninitialized properties
- **Files**: `SmartListDto.cs`, `SmartPlaylistDto.cs`, `SmartCollectionDto.cs`, `SortOption.cs`, `ExpressionSet.cs`, `OrderDto.cs`, `SmartList.cs`
- **Impact**: ~30 errors fixed (CS8618)
- **Risk**: Low (ensure JSON deserialization still works)
- **Time**: 1 hour

### Priority 3: Return Type Nullability (Medium Impact)
**Change**: Add `?` to return types that can return null
- **Files**: `SmartListFileSystem.cs`, `PlaylistStore.cs`, `PlaylistService.cs`, `ManualRefreshService.cs`, `ResolutionTypes.cs`, `Expression.cs`
- **Impact**: ~20 errors fixed (CS8603)
- **Risk**: Low-Medium
- **Time**: 1-2 hours

### Priority 4: Interface Implementations (Medium Impact)
**Change**: Fix IComparer, IComparable, and ILogger signatures
- **Files**: `SmartList.cs`, `SmartListController.cs`
- **Impact**: ~7 errors fixed (CS8767, CS8633)
- **Risk**: Low-Medium
- **Time**: 1 hour

### Priority 5: Delegate Signatures (Lower Impact)
**Change**: Fix event handler and timer callback signatures
- **Files**: `AutoRefreshService.cs`
- **Impact**: ~15 errors fixed (CS8622)
- **Risk**: Medium
- **Time**: 1 hour

## Tools and Resources

- **IDE**: Use Visual Studio or Rider nullable analysis
- **Roslyn Analyzers**: Already configured in csproj
- **Documentation**: 
  - [Nullable Reference Types (C#)](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references)
  - [Jellyfin Plugin Template](https://github.com/jellyfin/jellyfin-plugin-template)

## Notes

- **Null-forgiving operator (`!`)**: Use sparingly, only when we're certain a value won't be null
- **Null-conditional operator (`?.`)**: Use for safe member access
- **Null-coalescing operator (`??`)**: Use for providing default values
- **Guard clauses**: Add null checks at method entry points

## Success Criteria

- ✅ Project builds with `TreatWarningsAsErrors=true`
- ✅ All nullable reference type errors resolved
- ✅ No runtime null reference exceptions introduced
- ✅ Code maintains backward compatibility
- ✅ All existing functionality works as before

## Rollback Plan

If issues arise:
1. Temporarily set `TreatWarningsAsErrors=false` in csproj
2. Keep `Nullable=enable` to continue getting warnings
3. Fix issues incrementally without blocking builds
4. Re-enable `TreatWarningsAsErrors` once all issues are resolved

