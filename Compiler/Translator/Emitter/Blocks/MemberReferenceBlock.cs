using Bridge.Contract;
using Bridge.Contract.Constants;

using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using Object.Net.Utilities;

using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Bridge.Translator
{
    public class MemberReferenceBlock : ConversionBlock
    {
        public MemberReferenceBlock(IEmitter emitter, MemberReferenceExpression memberReferenceExpression)
            : base(emitter, memberReferenceExpression)
        {
            this.Emitter = emitter;
            this.MemberReferenceExpression = memberReferenceExpression;
        }

        public MemberReferenceExpression MemberReferenceExpression
        {
            get;
            set;
        }

        protected override Expression GetExpression()
        {
            return this.MemberReferenceExpression;
        }

        protected override void EmitConversionExpression()
        {
            this.VisitMemberReferenceExpression();
        }

        protected string WriteTarget(ResolveResult resolveResult, bool isInterfaceMember, MemberResolveResult memberTargetrr, ResolveResult targetrr, bool openParentheses)
        {
            string interfaceTempVar = null;
            if (isInterfaceMember)
            {
                bool isField = memberTargetrr != null && memberTargetrr.Member is IField && (memberTargetrr.TargetResult is ThisResolveResult || memberTargetrr.TargetResult is LocalResolveResult);

                if (!(targetrr is ThisResolveResult || targetrr is TypeResolveResult || targetrr is LocalResolveResult || isField))
                {
                    if (openParentheses)
                    {
                        this.WriteOpenParentheses();
                    }
                    
                    interfaceTempVar = this.GetTempVarName();
                    this.Write(interfaceTempVar);
                    this.Write(" = ");
                }
            }

            this.WriteSimpleTarget(resolveResult);

            return interfaceTempVar;
        }

        protected void WriteSimpleTarget(ResolveResult resolveResult)
        {
            MemberResolveResult member = resolveResult as MemberResolveResult;
            if (member == null || !member.Member.IsStatic)
            {
                this.MemberReferenceExpression.Target.AcceptVisitor(this.Emitter);
                return;
            }

            var imethod = member.Member as IMethod;
            var imember = member.Member;
            if ((imethod != null && imethod.IsExtensionMethod) || imember == null)
            {
                this.MemberReferenceExpression.Target.AcceptVisitor(this.Emitter);
                return;
            }

            this.Write(BridgeTypes.ToJsName(member.Member.DeclaringType, this.Emitter));
        }

        private void WriteInterfaceMember(string interfaceTempVar, MemberResolveResult resolveResult, bool isSetter, string prefix = null)
        {
            if (interfaceTempVar != null)
            {
                this.WriteComma();
                this.Write(interfaceTempVar);
            }

            var externalInterface = this.Emitter.Validator.IsExternalInterface(resolveResult.Member.DeclaringTypeDefinition);

            this.WriteOpenBracket();

            if (externalInterface)
            {
                this.Write(JS.Funcs.BRIDGE_GET_I);
                this.WriteOpenParentheses();

                if (interfaceTempVar != null)
                {
                    this.Write(interfaceTempVar);
                }
                else
                {
                    this.WriteSimpleTarget(resolveResult);
                }

                this.WriteComma();

                var interfaceName = OverloadsCollection.Create(Emitter, resolveResult.Member, isSetter).GetOverloadName(false, prefix);

                if (interfaceName.StartsWith("\""))
                {
                    this.Write(interfaceName);
                }
                else
                {
                    this.WriteScript(interfaceName);
                }
                
                this.WriteComma();
                this.WriteScript(OverloadsCollection.Create(Emitter, resolveResult.Member, isSetter).GetOverloadName(true, prefix));

                this.Write(")");
            }
            else
            {
                this.Write(OverloadsCollection.Create(Emitter, resolveResult.Member, isSetter).GetOverloadName(false, prefix));
            }
            
            this.WriteCloseBracket();

            if (interfaceTempVar != null)
            {
                this.WriteCloseParentheses();
            }
        }

        protected void VisitMemberReferenceExpression()
        {
            MemberReferenceExpression memberReferenceExpression = this.MemberReferenceExpression;
            int pos = this.Emitter.Output.Length;
            bool isRefArg = this.Emitter.IsRefArg;
            this.Emitter.IsRefArg = false;

            ResolveResult resolveResult = null;
            ResolveResult expressionResolveResult = null;
            string targetVar = null;
            string valueVar = null;
            bool isStatement = false;
            bool isConstTarget = false;

            var targetrr = this.Emitter.Resolver.ResolveNode(memberReferenceExpression.Target, this.Emitter);
            if (targetrr is ConstantResolveResult)
            {
                isConstTarget = true;
            }

            var memberTargetrr = targetrr as MemberResolveResult;
            if (memberTargetrr != null && memberTargetrr.Type.Kind == TypeKind.Enum && memberTargetrr.Member is DefaultResolvedField && this.Emitter.Validator.EnumEmitMode(memberTargetrr.Type) == 2)
            {
                isConstTarget = true;
            }

            if (memberReferenceExpression.Target is ParenthesizedExpression || 
                (targetrr is ConstantResolveResult && targetrr.Type.IsKnownType(KnownTypeCode.Int64)) ||
                (targetrr is ConstantResolveResult && targetrr.Type.IsKnownType(KnownTypeCode.UInt64)) ||
                (targetrr is ConstantResolveResult && targetrr.Type.IsKnownType(KnownTypeCode.Decimal)))
            {
                isConstTarget = false;
            }

            if (memberReferenceExpression.Parent is InvocationExpression && (((InvocationExpression)(memberReferenceExpression.Parent)).Target == memberReferenceExpression))
            {
                resolveResult = this.Emitter.Resolver.ResolveNode(memberReferenceExpression.Parent, this.Emitter);
                expressionResolveResult = this.Emitter.Resolver.ResolveNode(memberReferenceExpression, this.Emitter);

                if (expressionResolveResult is InvocationResolveResult)
                {
                    resolveResult = expressionResolveResult;
                }
                else if (expressionResolveResult is MemberResolveResult)
                {
                    if (((MemberResolveResult) expressionResolveResult).Member is IProperty)
                    {
                        resolveResult = expressionResolveResult;
                    }
                }
            }
            else
            {
                resolveResult = this.Emitter.Resolver.ResolveNode(memberReferenceExpression, this.Emitter);
            }

            bool oldIsAssignment = this.Emitter.IsAssignment;
            bool oldUnary = this.Emitter.IsUnaryAccessor;

            if (resolveResult == null)
            {
                this.Emitter.IsAssignment = false;
                this.Emitter.IsUnaryAccessor = false;
                if (isConstTarget)
                {
                    this.Write("(");
                }
                memberReferenceExpression.Target.AcceptVisitor(this.Emitter);
                if (isConstTarget)
                {
                    this.Write(")");
                }
                this.Emitter.IsAssignment = oldIsAssignment;
                this.Emitter.IsUnaryAccessor = oldUnary;
                this.WriteDot();
                string name = memberReferenceExpression.MemberName;
                this.Write(name.ToLowerCamelCase());

                return;
            }

            if (resolveResult is DynamicInvocationResolveResult)
            {
                resolveResult = ((DynamicInvocationResolveResult)resolveResult).Target;
            }

            if (resolveResult is MethodGroupResolveResult)
            {
                var oldResult = (MethodGroupResolveResult)resolveResult;
                resolveResult = this.Emitter.Resolver.ResolveNode(memberReferenceExpression.Parent, this.Emitter);

                if (resolveResult is DynamicInvocationResolveResult)
                {
                    var method = oldResult.Methods.Last();
                    resolveResult = new MemberResolveResult(new TypeResolveResult(method.DeclaringType), method);
                }
            }

            MemberResolveResult member = resolveResult as MemberResolveResult;
            var globalTarget = member != null ? this.Emitter.IsGlobalTarget(member.Member) : null;

            if (!(resolveResult is InvocationResolveResult) && member != null && member.Member is IMethod)
            {
                var interceptor = this.Emitter.Plugins.OnReference(this, this.MemberReferenceExpression, member);

                if (interceptor.Cancel)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(interceptor.Replacement))
                {
                    this.Write(interceptor.Replacement);
                    return;
                }
            }

            if (globalTarget != null && globalTarget.Item1)
            {
                var target = globalTarget.Item2;

                if (!string.IsNullOrWhiteSpace(target))
                {
                    bool assign = false;
                    var memberExpression = member.Member is IMethod ? memberReferenceExpression.Parent.Parent : memberReferenceExpression.Parent;
                    var targetExpression = member.Member is IMethod ? memberReferenceExpression.Parent : memberReferenceExpression;
                    var assignment = memberExpression as AssignmentExpression;
                    if (assignment != null && assignment.Right == targetExpression)
                    {
                        assign = true;
                    }
                    else
                    {
                        var varInit = memberExpression as VariableInitializer;
                        if (varInit != null && varInit.Initializer == targetExpression)
                        {
                            assign = true;
                        }
                        else if (memberExpression is InvocationExpression)
                        {
                            var targetInvocation = (InvocationExpression)memberExpression;
                            if (targetInvocation.Arguments.Any(a => a == targetExpression))
                            {
                                assign = true;
                            }
                        }
                    }

                    if (assign)
                    {
                        if (resolveResult is InvocationResolveResult)
                        {
                            this.PushWriter(target);
                        }
                        else
                        {
                            this.Write(target);
                        }

                        return;
                    }
                }

                if (resolveResult is InvocationResolveResult)
                {
                    this.PushWriter("");
                }

                return;
            }

            Tuple<bool, bool, string> inlineInfo = member != null ? this.Emitter.GetInlineCode(memberReferenceExpression) : null;
            //string inline = member != null ? this.Emitter.GetInline(member.Member) : null;
            string inline = inlineInfo != null ? inlineInfo.Item3 : null;
            bool hasInline = !string.IsNullOrEmpty(inline);
            bool hasThis = hasInline && inline.Contains("{this}");
            bool isInterfaceMember = false;

            if (hasInline && inline.StartsWith("<self>"))
            {
                hasThis = true;
                inline = inline.Substring(6);
            }

            if (inline == null && member != null && member.Member.DeclaringTypeDefinition != null &&
                member.Member.DeclaringTypeDefinition.Kind == TypeKind.Interface &&
                (this.Emitter.Validator.IsExternalInterface(member.Member.DeclaringTypeDefinition) || member.Member.DeclaringType.TypeArguments.Any(arg => arg.Kind == TypeKind.TypeParameter)))
            {
                isInterfaceMember = true;
            }

            string interfaceTempVar = null;

            if (hasThis)
            {
                this.Write("");
                var oldBuilder = this.Emitter.Output;
                this.Emitter.Output = new StringBuilder();
                this.Emitter.IsAssignment = false;
                this.Emitter.IsUnaryAccessor = false;
                if (isConstTarget)
                {
                    this.Write("(");
                }
                this.WriteSimpleTarget(resolveResult);
                if (isConstTarget)
                {
                    this.Write(")");
                }
                
                var oldInline = inline;
                var thisArg = this.Emitter.Output.ToString();
                int thisIndex = inline.IndexOf("{this}");
                inline = inline.Replace("{this}", thisArg);

                if (member != null && member.Member is IProperty)
                {
                    this.Emitter.Output = new StringBuilder();
                    inline = inline.Replace("{0}", "[[0]]");
                    new InlineArgumentsBlock(this.Emitter, new ArgumentsInfo(this.Emitter, memberReferenceExpression, resolveResult), inline).Emit();
                    inline = this.Emitter.Output.ToString();
                    inline = inline.Replace("[[0]]", "{0}");
                }

                this.Emitter.IsAssignment = oldIsAssignment;
                this.Emitter.IsUnaryAccessor = oldUnary;
                this.Emitter.Output = oldBuilder;

                int[] range = null;

                if (thisIndex > -1)
                {
                    range = new[] { thisIndex, thisIndex + thisArg.Length };
                }

                if (resolveResult is InvocationResolveResult)
                {
                    this.PushWriter(inline, null, thisArg, range);
                }
                else
                {
                    if (member != null && member.Member is IMethod)
                    {
                        new InlineArgumentsBlock(this.Emitter, new ArgumentsInfo(this.Emitter, memberReferenceExpression, resolveResult), oldInline, (IMethod)member.Member, targetrr).EmitFunctionReference();
                    }
                    else
                    {
                        this.Write(inline);  
                    }
                }

                return;
            }

            if (member != null && member.Member.SymbolKind == SymbolKind.Field && this.Emitter.IsMemberConst(member.Member) && this.Emitter.IsInlineConst(member.Member))
            {
                var parentExpression = memberReferenceExpression.Parent as MemberReferenceExpression;
                bool wrap = false;

                if (parentExpression != null)
                {
                    var ii = this.Emitter.GetInlineCode(parentExpression);

                    if (string.IsNullOrEmpty(ii.Item3))
                    {
                        wrap = true;
                        this.WriteOpenParentheses();
                    }
                }

                this.WriteScript(member.ConstantValue);

                if (wrap)
                {
                    this.WriteCloseParentheses();
                }
            }
            else if (hasInline && member.Member.IsStatic)
            {
                if (resolveResult is InvocationResolveResult)
                {
                    this.PushWriter(inline);
                }
                else
                {
                    if (member != null && member.Member is IMethod)
                    {
                        new InlineArgumentsBlock(this.Emitter, new ArgumentsInfo(this.Emitter, memberReferenceExpression, resolveResult), inline, (IMethod)member.Member, targetrr).EmitFunctionReference();
                    }
                    else
                    {
                        new InlineArgumentsBlock(this.Emitter, new ArgumentsInfo(this.Emitter, memberReferenceExpression, resolveResult), inline).Emit();
                    }
                }
            }
            else
            {
                if (member != null && member.IsCompileTimeConstant && member.Member.DeclaringType.Kind == TypeKind.Enum)
                {
                    var typeDef = member.Member.DeclaringType as DefaultResolvedTypeDefinition;

                    if (typeDef != null)
                    {
                        var enumMode = this.Emitter.Validator.EnumEmitMode(typeDef);

                        if ((this.Emitter.Validator.IsIgnoreType(typeDef) && enumMode == -1) || enumMode == 2)
                        {
                            this.WriteScript(member.ConstantValue);

                            return;
                        }

                        if (enumMode >= 3 && enumMode < 7)
                        {
                            string enumStringName = member.Member.Name;
                            var attr = Helpers.GetInheritedAttribute(member.Member, Translator.Bridge_ASSEMBLY + ".NameAttribute");

                            if (attr != null)
                            {
                                enumStringName = this.Emitter.GetEntityName(member.Member);
                            }
                            else
                            {
                                switch (enumMode)
                                {
                                    case 3:
                                        enumStringName = Object.Net.Utilities.StringUtils.ToLowerCamelCase(member.Member.Name);
                                        break;

                                    case 4:
                                        break;

                                    case 5:
                                        enumStringName = enumStringName.ToLowerInvariant();
                                        break;

                                    case 6:
                                        enumStringName = enumStringName.ToUpperInvariant();
                                        break;
                                }
                            }

                            this.WriteScript(enumStringName);

                            return;
                        }
                    }
                }

                if (resolveResult is TypeResolveResult)
                {
                    TypeResolveResult typeResolveResult = (TypeResolveResult)resolveResult;
                    this.Write(BridgeTypes.ToJsName(typeResolveResult.Type, this.Emitter));
                    return;
                }
                else if (member != null &&
                         member.Member is IMethod &&
                         !(member is InvocationResolveResult) &&
                         !(
                            memberReferenceExpression.Parent is InvocationExpression &&
                            memberReferenceExpression.NextSibling != null &&
                            memberReferenceExpression.NextSibling.Role is TokenRole &&
                            ((TokenRole)memberReferenceExpression.NextSibling.Role).Token == "("
                         )
                    )
                {
                    var resolvedMethod = (IMethod)member.Member;
                    bool isStatic = resolvedMethod != null && resolvedMethod.IsStatic;

                    var isExtensionMethod = resolvedMethod.IsExtensionMethod;

                    this.Emitter.IsAssignment = false;
                    this.Emitter.IsUnaryAccessor = false;

                    if (!isStatic)
                    {
                        this.Write(isExtensionMethod ? JS.Funcs.BRIDGE_BIND_SCOPE : JS.Funcs.BRIDGE_BIND);
                        this.WriteOpenParentheses();
                        interfaceTempVar = this.WriteTarget(resolveResult, isInterfaceMember, memberTargetrr, targetrr, false);
                        this.Write(", ");
                    }

                    this.Emitter.IsAssignment = oldIsAssignment;
                    this.Emitter.IsUnaryAccessor = oldUnary;

                    if (isExtensionMethod)
                    {
                        this.Write(BridgeTypes.ToJsName(resolvedMethod.DeclaringType, this.Emitter));
                    }
                    else
                    {
                        this.Emitter.IsAssignment = false;
                        this.Emitter.IsUnaryAccessor = false;
                        if (isConstTarget)
                        {
                            this.Write("(");
                        }

                        if (interfaceTempVar != null)
                        {
                            this.Write(interfaceTempVar);
                        }
                        else
                        {
                            this.WriteSimpleTarget(resolveResult);
                        }
                        
                        if (isConstTarget)
                        {
                            this.Write(")");
                        }
                        this.Emitter.IsAssignment = oldIsAssignment;
                        this.Emitter.IsUnaryAccessor = oldUnary;
                    }

                    if (isInterfaceMember)
                    {
                        this.WriteInterfaceMember(interfaceTempVar, member, false);
                    }
                    else
                    {
                        this.WriteDot();
                        this.Write(OverloadsCollection.Create(this.Emitter, member.Member).GetOverloadName());
                    }

                    if (!isStatic)
                    {
                        this.Write(")");
                    }

                    return;
                }
                else
                {
                    bool isProperty = false;

                    if (member != null && member.Member.SymbolKind == SymbolKind.Property && member.TargetResult.Type.Kind != TypeKind.Anonymous && !this.Emitter.Validator.IsObjectLiteral(member.Member.DeclaringTypeDefinition))
                    {
                        isProperty = true;
                        bool writeTargetVar = false;

                        if (this.Emitter.IsAssignment && this.Emitter.AssignmentType != AssignmentOperatorType.Assign)
                        {
                            writeTargetVar = true;
                        }
                        else if (this.Emitter.IsUnaryAccessor)
                        {
                            writeTargetVar = true;

                            isStatement = memberReferenceExpression.Parent is UnaryOperatorExpression && memberReferenceExpression.Parent.Parent is ExpressionStatement;

                            if (NullableType.IsNullable(member.Type))
                            {
                                isStatement = false;
                            }

                            if (!isStatement)
                            {
                                this.WriteOpenParentheses();
                            }
                        }

                        if (writeTargetVar)
                        {
                            bool isField = memberTargetrr != null && memberTargetrr.Member is IField && (memberTargetrr.TargetResult is ThisResolveResult || memberTargetrr.TargetResult is LocalResolveResult);

                            if (!(targetrr is ThisResolveResult || targetrr is TypeResolveResult || targetrr is LocalResolveResult || isField))
                            {
                                targetVar = this.GetTempVarName();

                                this.Write(targetVar);
                                this.Write(" = ");
                            }
                        }
                    }

                    if (isProperty && this.Emitter.IsUnaryAccessor && !isStatement && targetVar == null)
                    {
                        valueVar = this.GetTempVarName();

                        this.Write(valueVar);
                        this.Write(" = ");
                    }

                    this.Emitter.IsAssignment = false;
                    this.Emitter.IsUnaryAccessor = false;
                    if (isConstTarget)
                    {
                        this.Write("(");
                    }

                    if (targetVar == null && isInterfaceMember)
                    {
                        interfaceTempVar = this.WriteTarget(resolveResult, isInterfaceMember, memberTargetrr, targetrr, true);
                    }
                    else
                    {
                        this.WriteSimpleTarget(resolveResult);
                    }
                    
                    if (isConstTarget)
                    {
                        this.Write(")");
                    }
                    this.Emitter.IsAssignment = oldIsAssignment;
                    this.Emitter.IsUnaryAccessor = oldUnary;

                    if (targetVar != null)
                    {
                        if (this.Emitter.IsUnaryAccessor && !isStatement)
                        {
                            this.WriteComma(false);

                            valueVar = this.GetTempVarName();

                            this.Write(valueVar);
                            this.Write(" = ");

                            this.Write(targetVar);
                        }
                        else
                        {
                            this.WriteSemiColon();
                            this.WriteNewLine();
                            this.Write(targetVar);
                        }
                    }
                }

                var targetResolveResult = targetrr as MemberResolveResult;

                if (targetResolveResult == null || this.Emitter.IsGlobalTarget(targetResolveResult.Member) == null)
                {
                    if (isRefArg)
                    {
                        this.WriteComma();
                    }
                    else if (!isInterfaceMember)
                    {
                        this.WriteDot();    
                    }
                }

                if (member == null)
                {
                    if (targetrr != null && targetrr.Type.Kind == TypeKind.Dynamic)
                    {
                        this.Write(memberReferenceExpression.MemberName);
                    }
                    else
                    {
                        this.Write(memberReferenceExpression.MemberName.ToLowerCamelCase());
                    }
                }
                else if (!string.IsNullOrEmpty(inline))
                {
                    if (resolveResult is InvocationResolveResult || (member.Member.SymbolKind == SymbolKind.Property && this.Emitter.IsAssignment))
                    {
                        this.PushWriter(inline);
                    }
                    else
                    {
                        this.Write(inline);
                    }
                }
                else if (member.Member.SymbolKind == SymbolKind.Property && member.TargetResult.Type.Kind != TypeKind.Anonymous && !this.Emitter.Validator.IsObjectLiteral(member.Member.DeclaringTypeDefinition))
                {
                    var proto = false;
                    if (this.MemberReferenceExpression.Target is BaseReferenceExpression && member != null)
                    {
                        var prop = member.Member as IProperty;

                        if (prop != null && (prop.IsVirtual || prop.IsOverride))
                        {
                            proto = true;
                        }
                    }

                    bool isFieldProperty = Helpers.IsFieldProperty(member.Member, this.Emitter);
                    if (isFieldProperty)
                    {
                        if (member.Member.ImplementedInterfaceMembers.Count > 0 &&
                            !member.Member.ImplementedInterfaceMembers.All(m => Helpers.IsFieldProperty(m, this.Emitter)))
                        {
                            throw new EmitterException(memberReferenceExpression,
                                string.Format(
                                    "The property {0} is marked as FieldProperty but implemented interface member has no such attribute",
                                    member.Member.ToString()));
                        }
                    }
                    else
                    {
                        if (member.Member.ImplementedInterfaceMembers.Count > 0 && member.Member.ImplementedInterfaceMembers.Any(m => Helpers.IsFieldProperty(m, this.Emitter)))
                        {
                            throw new EmitterException(memberReferenceExpression, string.Format("The property {0} is not marked as FieldProperty but implemented interface member has such attribute", member.Member.ToString()));
                        }
                    }

                    if (isFieldProperty)
                    {
                        if (isInterfaceMember)
                        {
                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                        }
                        else
                        {
                            this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter));
                        }
                    }
                    else if (!this.Emitter.IsAssignment)
                    {
                        if (this.Emitter.IsUnaryAccessor)
                        {
                            bool isNullable = NullableType.IsNullable(member.Member.ReturnType);
                            bool isDecimal = Helpers.IsDecimalType(member.Member.ReturnType, this.Emitter.Resolver);
                            bool isLong = Helpers.Is64Type(member.Member.ReturnType, this.Emitter.Resolver);

                            if (isStatement)
                            {
                                if (isInterfaceMember)
                                {
                                    this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, true, JS.Funcs.Property.SET);
                                }
                                else
                                {
                                    this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter, true));
                                }

                                if (proto)
                                {
                                    this.WriteCall();
                                    this.WriteOpenParentheses();
                                    this.WriteThis();
                                    this.WriteComma();
                                }
                                else
                                {
                                    this.WriteOpenParentheses();
                                }

                                if (isDecimal || isLong)
                                {
                                    if (isNullable)
                                    {
                                        this.Write(JS.Types.SYSTEM_NULLABLE + "." + JS.Funcs.Math.LIFT1);
                                        this.WriteOpenParentheses();
                                        if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                                        {
                                            this.WriteScript(JS.Funcs.Math.INC);
                                        }
                                        else
                                        {
                                            this.WriteScript(JS.Funcs.Math.DEC);
                                        }

                                        this.WriteComma();

                                        if (targetVar != null)
                                        {
                                            this.Write(targetVar);
                                        }
                                        else if (interfaceTempVar != null)
                                        {
                                            this.Write(interfaceTempVar);
                                        }
                                        else
                                        {
                                            this.WriteSimpleTarget(resolveResult);
                                        }

                                        if (isInterfaceMember)
                                        {
                                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                                        }
                                        else
                                        {
                                            this.WriteDot();
                                            this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter, false));
                                        }

                                        if (proto)
                                        {
                                            this.WriteCall();
                                            this.WriteOpenParentheses();
                                            this.WriteThis();
                                            this.WriteCloseParentheses();
                                        }
                                        else
                                        {
                                            this.WriteOpenParentheses();
                                            this.WriteCloseParentheses();
                                        }

                                        this.WriteCloseParentheses();
                                    }
                                    else
                                    {
                                        if (targetVar != null || interfaceTempVar != null)
                                        {
                                            this.Write(targetVar ?? interfaceTempVar);
                                        }
                                        else
                                        {
                                            this.WriteSimpleTarget(resolveResult);
                                        }

                                        if (isInterfaceMember)
                                        {
                                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                                        }
                                        else
                                        {
                                            this.WriteDot();
                                            this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter, false));
                                        }

                                        if (proto)
                                        {
                                            this.WriteCall();
                                            this.WriteOpenParentheses();
                                            this.WriteThis();
                                            this.WriteCloseParentheses();
                                        }
                                        else
                                        {
                                            this.WriteOpenParentheses();
                                            this.WriteCloseParentheses();
                                        }

                                        this.WriteDot();

                                        if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                                        {
                                            this.Write(JS.Funcs.Math.INC);
                                        }
                                        else
                                        {
                                            this.Write(JS.Funcs.Math.DEC);
                                        }

                                        this.WriteOpenParentheses();
                                        this.WriteCloseParentheses();

                                        this.WriteCloseParentheses();
                                    }
                                }
                                else
                                {
                                    if (targetVar != null || interfaceTempVar != null)
                                    {
                                        this.Write(targetVar ?? interfaceTempVar);
                                    }
                                    else
                                    {
                                        if (isConstTarget)
                                        {
                                            this.Write("(");
                                        }
                                        this.WriteSimpleTarget(resolveResult);
                                        if (isConstTarget)
                                        {
                                            this.Write(")");
                                        }
                                    }

                                    if (isInterfaceMember)
                                    {
                                        this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                                    }
                                    else
                                    {
                                        this.WriteDot();
                                        this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter, false));
                                    }

                                    if (proto)
                                    {
                                        this.WriteCall();
                                        this.WriteOpenParentheses();
                                        this.WriteThis();
                                        this.WriteCloseParentheses();
                                    }
                                    else
                                    {
                                        this.WriteOpenParentheses();
                                        this.WriteCloseParentheses();
                                    }

                                    if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                                    {
                                        this.Write("+");
                                    }
                                    else
                                    {
                                        this.Write("-");
                                    }

                                    this.Write("1");
                                    this.WriteCloseParentheses();
                                }
                            }
                            else
                            {
                                if (isInterfaceMember)
                                {
                                    this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                                }
                                else
                                {
                                    this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter, false));
                                }

                                if (proto)
                                {
                                    this.WriteCall();
                                    this.WriteOpenParentheses();
                                    this.WriteThis();
                                    this.WriteCloseParentheses();
                                }
                                else
                                {
                                    this.WriteOpenParentheses();
                                    this.WriteCloseParentheses();
                                }
                                this.WriteComma();

                                if (targetVar != null || interfaceTempVar != null)
                                {
                                    this.Write(targetVar ?? interfaceTempVar);
                                }
                                else
                                {
                                    if (isConstTarget)
                                    {
                                        this.Write("(");
                                    }
                                    this.WriteSimpleTarget(resolveResult);
                                    if (isConstTarget)
                                    {
                                        this.Write(")");
                                    }
                                }

                                if (isInterfaceMember)
                                {
                                    this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, true, JS.Funcs.Property.SET);
                                }
                                else
                                {
                                    this.WriteDot();
                                    this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter, true));
                                }

                                if (proto)
                                {
                                    this.WriteCall();
                                    this.WriteOpenParentheses();
                                    this.WriteThis();
                                    this.WriteComma();
                                }
                                else
                                {
                                    this.WriteOpenParentheses();
                                }

                                if (isDecimal || isLong)
                                {
                                    if (isNullable)
                                    {
                                        this.Write(JS.Types.SYSTEM_NULLABLE + "." + JS.Funcs.Math.LIFT1);
                                        this.WriteOpenParentheses();
                                        if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                                        {
                                            this.WriteScript(JS.Funcs.Math.INC);
                                        }
                                        else
                                        {
                                            this.WriteScript(JS.Funcs.Math.DEC);
                                        }
                                        this.WriteComma();
                                        this.Write(valueVar);
                                        this.WriteCloseParentheses();
                                    }
                                    else
                                    {
                                        this.Write(valueVar);
                                        this.WriteDot();
                                        if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                                        {
                                            this.Write(JS.Funcs.Math.INC);
                                        }
                                        else
                                        {
                                            this.Write(JS.Funcs.Math.DEC);
                                        }
                                        this.WriteOpenParentheses();
                                        this.WriteCloseParentheses();
                                    }
                                }
                                else
                                {
                                    this.Write(valueVar);

                                    if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                                    {
                                        this.Write("+");
                                    }
                                    else
                                    {
                                        this.Write("-");
                                    }
                                    this.Write("1");
                                }

                                this.WriteCloseParentheses();
                                this.WriteComma();

                                bool isPreOp = this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment ||
                                               this.Emitter.UnaryOperatorType == UnaryOperatorType.Decrement;

                                if (isPreOp)
                                {
                                    if (targetVar != null || interfaceTempVar != null)
                                    {
                                        this.Write(targetVar ?? interfaceTempVar);
                                    }
                                    else
                                    {
                                        this.WriteSimpleTarget(resolveResult);
                                    }

                                    if (isInterfaceMember)
                                    {
                                        this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                                    }
                                    else
                                    {
                                        this.WriteDot();
                                        this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter, false));
                                    }
                                    
                                    this.WriteOpenParentheses();
                                    this.WriteCloseParentheses();
                                }
                                else
                                {
                                    this.Write(valueVar);
                                }
                                this.WriteCloseParentheses();

                                if (valueVar != null)
                                {
                                    this.RemoveTempVar(valueVar);
                                }
                            }

                            if (targetVar != null)
                            {
                                this.RemoveTempVar(targetVar);
                            }
                        }
                        else
                        {
                            if (isInterfaceMember)
                            {
                                this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                            }
                            else
                            {
                                this.Write(Helpers.GetPropertyRef(member.Member, this.Emitter));
                            }
                            
                            if (proto)
                            {
                                this.WriteCall();
                                this.WriteOpenParentheses();
                                this.WriteThis();
                                this.WriteCloseParentheses();
                            }
                            else
                            {
                                this.WriteOpenParentheses();
                                this.WriteCloseParentheses();
                            }
                        }
                    }
                    else if (this.Emitter.AssignmentType != AssignmentOperatorType.Assign)
                    {
                        string memberStr;
                        if (isInterfaceMember)
                        {
                            var oldWriter = this.SaveWriter();
                            this.NewWriter();

                            this.Emitter.IsAssignment = false;
                            this.Emitter.IsUnaryAccessor = false;
                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, true, JS.Funcs.Property.SET);
                            this.Emitter.IsAssignment = oldIsAssignment;
                            this.Emitter.IsUnaryAccessor = oldUnary;
                            memberStr = this.Emitter.Output.ToString();
                            this.RestoreWriter(oldWriter);
                        }
                        else
                        {
                            memberStr = Helpers.GetPropertyRef(member.Member, this.Emitter, true);
                        }

                        string getterMember;
                        if (isInterfaceMember)
                        {
                            var oldWriter = this.SaveWriter();
                            this.NewWriter();

                            this.Emitter.IsAssignment = false;
                            this.Emitter.IsUnaryAccessor = false;
                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false, JS.Funcs.Property.GET);
                            this.Emitter.IsAssignment = oldIsAssignment;
                            this.Emitter.IsUnaryAccessor = oldUnary;
                            getterMember = this.Emitter.Output.ToString();
                            this.RestoreWriter(oldWriter);
                        }
                        else
                        {
                            getterMember = "." + Helpers.GetPropertyRef(member.Member, this.Emitter, false);
                        }

                        if (targetVar != null)
                        {
                            this.PushWriter(string.Concat(memberStr,
                                proto ? "." + JS.Funcs.CALL + "(this, " : "(",
                                targetVar,
                                getterMember,
                                proto ? "." + JS.Funcs.CALL + "(this)" : "()",
                                "{0})"), () =>
                                {
                                    this.RemoveTempVar(targetVar);
                                });
                        }
                        else
                        {
                            var oldWriter = this.SaveWriter();
                            this.NewWriter();

                            this.Emitter.IsAssignment = false;
                            this.Emitter.IsUnaryAccessor = false;
                            this.WriteSimpleTarget(resolveResult);
                            this.Emitter.IsAssignment = oldIsAssignment;
                            this.Emitter.IsUnaryAccessor = oldUnary;
                            var trg = this.Emitter.Output.ToString();

                            this.RestoreWriter(oldWriter);
                            this.PushWriter(string.Concat(memberStr,
                                proto ? "." + JS.Funcs.CALL + "(this, " : "(",
                                trg,
                                getterMember,
                                proto ? "." + JS.Funcs.CALL + "(this)" : "()",
                                "{0})"));
                        }
                    }
                    else
                    {
                        string trg;
                        if (isInterfaceMember)
                        {
                            var oldWriter = this.SaveWriter();
                            this.NewWriter();

                            this.Emitter.IsAssignment = false;
                            this.Emitter.IsUnaryAccessor = false;
                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, true, JS.Funcs.Property.SET);
                            this.Emitter.IsAssignment = oldIsAssignment;
                            this.Emitter.IsUnaryAccessor = oldUnary;
                            trg = this.Emitter.Output.ToString();
                            this.RestoreWriter(oldWriter);
                        }
                        else
                        {
                            trg = Helpers.GetPropertyRef(member.Member, this.Emitter, true);
                        }

                        this.PushWriter(trg + (proto ? ".call(this, {0})" : "({0})"));
                    }
                }
                else if (member.Member.SymbolKind == SymbolKind.Field)
                {
                    bool isConst = this.Emitter.IsMemberConst(member.Member);

                    if (isConst && this.Emitter.IsInlineConst(member.Member))
                    {
                        this.WriteScript(member.ConstantValue);
                    }
                    else
                    {
                        if (isInterfaceMember)
                        {
                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false);
                        }
                        else
                        {
                            var fieldName = OverloadsCollection.Create(this.Emitter, member.Member).GetOverloadName();
                            if (isRefArg)
                            {
                                this.WriteScript(fieldName);
                            }
                            else
                            {
                                this.Write(fieldName);
                            }
                        }
                    }
                }
                else if (resolveResult is InvocationResolveResult)
                {
                    InvocationResolveResult invocationResult = (InvocationResolveResult)resolveResult;
                    CSharpInvocationResolveResult cInvocationResult = (CSharpInvocationResolveResult)resolveResult;
                    var expresssionMember = expressionResolveResult as MemberResolveResult;

                    if (isInterfaceMember)
                    {
                        this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false);
                    }
                    else if (expresssionMember != null &&
                        cInvocationResult != null &&
                        cInvocationResult.IsDelegateInvocation &&
                        invocationResult.Member != expresssionMember.Member)
                    {
                        this.Write(OverloadsCollection.Create(this.Emitter, expresssionMember.Member).GetOverloadName());
                    }
                    else
                    {
                        this.Write(OverloadsCollection.Create(this.Emitter, invocationResult.Member).GetOverloadName());
                    }
                }
                else if (member.Member is DefaultResolvedEvent)
                {
                    if (this.Emitter.IsAssignment &&
                        (this.Emitter.AssignmentType == AssignmentOperatorType.Add ||
                         this.Emitter.AssignmentType == AssignmentOperatorType.Subtract))
                    {
                        if (isInterfaceMember)
                        {
                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, this.Emitter.AssignmentType == AssignmentOperatorType.Subtract, Helpers.GetAddOrRemove(this.Emitter.AssignmentType == AssignmentOperatorType.Add));
                        }
                        else
                        {
                            this.Write(Helpers.GetEventRef(member.Member, this.Emitter, this.Emitter.AssignmentType != AssignmentOperatorType.Add));
                        }
                            
                        this.WriteOpenParentheses();
                    }
                    else
                    {
                        if (isInterfaceMember)
                        {
                            this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false);
                        }
                        else
                        {
                            this.Write(this.Emitter.GetEntityName(member.Member, true));
                        }
                    }
                }
                else
                {
                    if (isInterfaceMember)
                    {
                        this.WriteInterfaceMember(interfaceTempVar ?? targetVar, member, false);
                    }
                    else
                    {
                        this.Write(this.Emitter.GetEntityName(member.Member));
                    }
                }

                Helpers.CheckValueTypeClone(resolveResult, memberReferenceExpression, this, pos);
            }
        }
    }
}
