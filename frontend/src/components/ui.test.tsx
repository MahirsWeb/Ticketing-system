import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ErrorText, Field, Input, Label, Select, SuccessText, Textarea } from './ui';

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

describe('messages are announced', () => {
  it('an error is an alert and a confirmation is a status, so a screen reader hears them without moving focus', () => {
    render(
      <>
        <ErrorText>It failed</ErrorText>
        <SuccessText>It worked</SuccessText>
      </>
    );

    expect(screen.getByRole('alert')).toHaveTextContent('It failed');
    expect(screen.getByRole('status')).toHaveTextContent('It worked');
  });

  it('renders nothing when there is nothing to say', () => {
    const { container } = render(
      <>
        <ErrorText />
        <SuccessText>{''}</SuccessText>
      </>
    );

    expect(container).toBeEmptyDOMElement();
  });
});
