﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mono.Cecil;
using Patchwork.Attributes;
using Patchwork.Collections;
using Patchwork.Utility;

namespace Patchwork {
	public partial class AssemblyPatcher {
		private SimpleTypeLookup<MemberAction<T>> OrganizeMembers<T>(SimpleTypeLookup<TypeAction> organizedTypes,
			Func<TypeDefinition, IEnumerable<T>> getMembers, Func<MemberReference, bool> filter) where T : MemberReference, IMemberDefinition {

			var memberSeq =
				from pair in organizedTypes.SelectMany(x => x)
				from yourMember in getMembers(pair.YourType)
				where !yourMember.HasCustomAttribute<DisablePatchingAttribute>()
				where filter(yourMember)
				let actionAttr = GetMemberActionAttribute(yourMember, pair.ActionAttribute)
				where actionAttr != null
					
				group new MemberAction<T>() {
					YourMember = yourMember,
					ActionAttribute = actionAttr,
					TypeAction = pair
				} by actionAttr.GetType();

			return memberSeq.ToSimpleTypeLookup();
		}

		private void ImplicitlyAddNewMethods<T>(SimpleTypeLookup<MemberAction<MethodDefinition>> methodActions, MemberAction<T> rootMemberAction,
			Func<T, IEnumerable<MethodDefinition>> getMethods) where T : IMemberDefinition {
			var newMethods = getMethods(rootMemberAction.YourMember);
			var allMethods = new HashSet<MethodDefinition>(methodActions.SelectMany(x => x).Select(x => x.YourMember));
			foreach (var method in newMethods) {
				if (allMethods.Contains(method)) continue;
				methodActions.GetGroup(typeof (NewMemberAttribute)).Values.Add(new MemberAction<MethodDefinition>() {
					YourMember = method,
					ActionAttribute = new NewMemberAttribute(true),
					TypeAction = rootMemberAction.TypeAction
				});
			}

		}

		private static Func<MemberReference, bool> CreateMemberFilter(DisablePatchingByNameAttribute attribute) {
			var regex = new Regex(attribute.Regex);
			Func<MemberReference, bool> memberFilter = member => {
				var notNotMatch = !regex.Match(member.FullName).Success;
				if (member is PropertyReference) {
					return notNotMatch || !attribute.Target.HasFlag(PatchingTarget.Property);
				} else if (member is MethodReference) {
					return notNotMatch || !attribute.Target.HasFlag(PatchingTarget.Method);
				} else if (member is EventReference) {
					return notNotMatch || !attribute.Target.HasFlag(PatchingTarget.Event);
				} else if (member is FieldReference) {
					return notNotMatch || !attribute.Target.HasFlag(PatchingTarget.Field);
				} else if (member is TypeReference) {
					return notNotMatch || !attribute.Target.HasFlag(PatchingTarget.Type);
				} else {
					throw new PatchInternalException($"Unknown member type: {member.GetType()}");
				}
			};
			return memberFilter;
		}

		public PatchingManifest CreateManifest(AssemblyDefinition yourAssembly) {
			var multicast = yourAssembly.GetCustomAttributes<DisablePatchingByNameAttribute>();
			var filter = CreateMemberFilter(multicast);

			if (!yourAssembly.IsPatchingAssembly()) {
				throw new PatchDeclerationException(
					"The assembly MUST have the PatchAssemblyAttribute attribute. Sorry.");
			}

			var allTypesInOrder = GetAllTypesInNestingOrder(yourAssembly.MainModule.Types).ToList();
			//+ Organizing types by action attribute
			var typesByActionSeq =
				from type in allTypesInOrder
				let typeActionAttr = GetTypeActionAttribute(type)
				where typeActionAttr != null && Filter(type) && filter(type)
				orderby type.UserFriendlyName()
				group new TypeAction() {
					YourType = type,
					ActionAttribute = typeActionAttr,
					TargetType = typeActionAttr is NewTypeAttribute ? null : GetPatchedTypeByName(type)
				} by typeActionAttr.GetType();

			var types = typesByActionSeq.ToSimpleTypeLookup();

			var fields = OrganizeMembers(types, x => x.Fields, filter);
			var methods = OrganizeMembers(types, x => x.Methods, filter);
			var properties = OrganizeMembers(types, x => x.Properties, filter);
			var events = OrganizeMembers(types, x => x.Events, filter);

			foreach (var eventAction in events[typeof (NewMemberAttribute)]) {
				ImplicitlyAddNewMethods(methods, eventAction, vent => {
					return new[] {
						vent.AddMethod,
						vent.RemoveMethod,
						vent.InvokeMethod,
					}.Concat(vent.OtherMethods).Where(method => method != null);
				});
			}

			foreach (var propAction in properties[typeof (NewMemberAttribute)]) {
				ImplicitlyAddNewMethods(methods, propAction, prop => {
					return new[] {
						prop.GetMethod,
						prop.SetMethod,
					}.Concat(prop.OtherMethods).Where(method => method != null);
				});
			}

			var patchingManifest = new PatchingManifest() {
				EventActions = events,
				FieldActions = fields,
				PropertyActions = properties,
				MethodActions = methods,
				TypeActions = types,
				PatchingAssembly = yourAssembly
			};

			return patchingManifest;
		}

		private static Func<MemberReference, bool> CreateMemberFilter(IEnumerable<DisablePatchingByNameAttribute> attributes) {
			return member => attributes.Select(CreateMemberFilter).All(f => f(member));
		}

		private TypeActionAttribute GetTypeActionAttribute(TypeDefinition provider) {
			var attr = provider.GetCustomAttribute<TypeActionAttribute>();
			if (attr != null) {
				return attr;
			}
			switch (ImplicitImports) {
				case ImplicitImportSetting.OnlyCompilerGenerated:
					if (provider.IsCompilerGenerated()) {
						goto case ImplicitImportSetting.ImplicitByDefault;
					}
					goto case ImplicitImportSetting.NoImplicit;
				case ImplicitImportSetting.ImplicitByDefault:
					return new NewTypeAttribute(true);
				default:
				case ImplicitImportSetting.NoImplicit:
					return null;
			}
		}

		private MemberActionAttribute GetMemberActionAttribute(IMemberDefinition provider,
			TypeActionAttribute typeAttr) {
			var attr = provider.GetCustomAttribute<MemberActionAttribute>();
			if (attr != null) {
				if (attr is ModifiesMemberAttribute && !(typeAttr is ModifiesTypeAttribute)) {
					throw Errors.Invalid_decleration("ModifiesMember is only legal inside ModifiesType.");
				}
				return attr;
			}
			if (typeAttr is NewTypeAttribute || typeAttr is ReplaceTypeAttribute) {
				return new NewMemberAttribute();
			}
			switch (ImplicitImports) {
				case ImplicitImportSetting.OnlyCompilerGenerated:
					if (provider.IsCompilerGenerated()) {
						goto case ImplicitImportSetting.ImplicitByDefault;
					}
					goto case ImplicitImportSetting.NoImplicit;
				case ImplicitImportSetting.ImplicitByDefault:
					return new NewMemberAttribute(true);
				default:
				case ImplicitImportSetting.NoImplicit:
					return null;
			}
		}

		private IEnumerable<TypeDefinition> GetAllTypesInNestingOrder(
			ICollection<TypeDefinition> currentNestingLevelTypes) {
			if (currentNestingLevelTypes.Count == 0) {
				yield break;
			}
			var stack = new List<TypeDefinition>();
			foreach (var type in currentNestingLevelTypes) {
				if ((type.HasCustomAttribute<DisablePatchingAttribute>() || !type.HasCustomAttribute<PatchingAttribute>()) && !type.IsCompilerGenerated()) {
					continue;
				}
				stack.AddRange(type.NestedTypes);
				yield return type;
			}
			foreach (var nestedType in GetAllTypesInNestingOrder(stack)) {
				yield return nestedType;
			}
		}
	}
}
