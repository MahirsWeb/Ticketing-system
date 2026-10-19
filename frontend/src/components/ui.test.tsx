import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Field, Input, Label, Select, Textarea } from './ui';

describe('Field', () => {
  it('connects the label to the control, so the control has a name', () => {
    render(
      <Field>
        <Label>Email</Label>
        <Input />
      </Field>
    );

    expect(screen.getByLabelText('Email')).toBeInstanceOf(HTMLInputElement);
  });

  it('focuses the control when the label is clicked', async () => {
    render(
      <Field>
        <Label>Notes</Label>
        <Textarea />
      </Field>
    );

    await userEvent.click(screen.getByText('Notes'));

    expect(screen.getByLabelText('Notes')).toHaveFocus();
  });

  it('works for a select too, and each field gets its own id', () => {
    render(
      <>
        <Field>
          <Label>Branch</Label>
          <Select>
            <option>A</option>
          </Select>
        </Field>
        <Field>
          <Label>Name</Label>
          <Input />
        </Field>
      </>
    );

    const branch = screen.getByLabelText('Branch');
    const name = screen.getByLabelText('Name');
    expect(branch).not.toBe(name);
    expect(branch.id).not.toBe(name.id);
  });

  it('does not invent an id for a control used outside a field', () => {
    render(<Input aria-label="Search" />);

    expect(screen.getByLabelText('Search')).not.toHaveAttribute('id');
  });
});
