// Copyright 2016 Marapongo, Inc. All rights reserved.

package eval

import (
	"fmt"
	"strconv"

	"github.com/marapongo/mu/pkg/compiler/symbols"
	"github.com/marapongo/mu/pkg/compiler/types"
	"github.com/marapongo/mu/pkg/tokens"
	"github.com/marapongo/mu/pkg/util/contract"
)

// Object is a value allocated and stored on the heap.  In MuIL's interpreter, all values are heap allocated, since we
// are less concerned about performance of the evaluation (compared to the cost of provisioning cloud resources).
type Object struct {
	t          symbols.Type // the runtime type of the object.
	value      Value        // any constant data associated with this object.
	properties Properties   // the full set of known properties and their values.
}

var _ fmt.Stringer = (*Object)(nil)

type Value interface{}                   // a literal object value.
type Properties map[tokens.Name]*Pointer // an object's properties.

// NewObject allocates a new object with the given type, primitive value, and properties.
func NewObject(t symbols.Type, value Value, properties Properties) *Object {
	return &Object{t: t, value: value, properties: properties}
}

func (o *Object) Type() symbols.Type     { return o.t }
func (o *Object) Value() Value           { return o.value }
func (o *Object) Properties() Properties { return o.properties }

// BoolValue asserts that the target is a boolean literal and returns its value.
func (o *Object) BoolValue() bool {
	contract.Assertf(o.t == types.Bool, "Expected object type to be Bool; got %v", o.t)
	contract.Assertf(o.value != nil, "Expected Bool object to carry a Value; got nil")
	b, ok := o.value.(bool)
	contract.Assertf(ok, "Expected Bool object's Value to be boolean literal")
	return b
}

// NumberValue asserts that the target is a numeric literal and returns its value.
func (o *Object) NumberValue() float64 {
	contract.Assertf(o.t == types.Number, "Expected object type to be Number; got %v", o.t)
	contract.Assertf(o.value != nil, "Expected Number object to carry a Value; got nil")
	n, ok := o.value.(float64)
	contract.Assertf(ok, "Expected Number object's Value to be numeric literal")
	return n
}

// StringValue asserts that the target is a string and returns its value.
func (o *Object) StringValue() string {
	contract.Assertf(o.t == types.String, "Expected object type to be String; got %v", o.t)
	contract.Assertf(o.value != nil, "Expected String object to carry a Value; got nil")
	s, ok := o.value.(string)
	contract.Assertf(ok, "Expected String object's Value to be string")
	return s
}

// FunctionValue asserts that the target is a reference and returns its value.
func (o *Object) FunctionValue() funcStub {
	contract.Assertf(o.value != nil, "Expected Function object to carry a Value; got nil")
	r, ok := o.value.(funcStub)
	contract.Assertf(ok, "Expected Function object's Value to be a Function")
	return r
}

// PointerValue asserts that the target is a reference and returns its value.
func (o *Object) PointerValue() *Pointer {
	contract.Assertf(o.value != nil, "Expected Pointer object to carry a Value; got nil")
	r, ok := o.value.(*Pointer)
	contract.Assertf(ok, "Expected Pointer object's Value to be a Pointer")
	return r
}

// GetPropertyAddr returns the reference to an object's property, lazily initializing if 'init' is true, or
// returning nil otherwise.
func (o *Object) GetPropertyAddr(nm tokens.Name, init bool) *Pointer {
	ref, has := o.properties[nm]
	if !has {
		ref = &Pointer{}
		o.properties[nm] = ref
	}
	return ref
}

// String can be used to print the contents of an object; it tries to be smart about the display.
func (o *Object) String() string {
	switch o.t {
	case types.Bool:
		if o.BoolValue() {
			return "true"
		}
		return "false"
	case types.String:
		return "\"" + o.StringValue() + "\""
	case types.Number:
		// TODO: it'd be nice to format as ints if the decimal part is close enough to "nothing".
		return strconv.FormatFloat(o.NumberValue(), 'f', -1, 64)
	case types.Null:
		return "<nil>"
	default:
		// See if it's a func; if yes, do function formatting.
		if _, isfnc := o.t.(*symbols.FunctionType); isfnc {
			stub := o.FunctionValue()
			var this string
			if stub.This == nil {
				this = "<nil>"
			} else {
				this = stub.This.String()
			}
			return "func{this=" + this +
				",type=" + stub.Func.FuncType().String() +
				",targ=" + stub.Func.Token().String() + "}"
		}

		// See if it's a pointer; if yes, format the reference.
		if _, isptr := o.t.(*symbols.PointerType); isptr {
			return o.PointerValue().String()
		}

		// Otherwise it's an arbitrary object; just dump out the type and properties.
		var p string
		for prop, ptr := range o.properties {
			if p != "" {
				p += ","
			}
			p += prop.String() + "=" + ptr.String()
		}
		return "obj{type=" + o.t.Token().String() + ",props={" + p + "}}"
	}
}
